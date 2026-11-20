using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SmartGuard.Configuration;

public static class PowerPlanCatalogProvider
{
  public static readonly Guid HighPerformancePlanGuid = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
  public static readonly Guid BalancedPlanGuid = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
  public static readonly Guid PowerSaverPlanGuid = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a");

  public const string HighPerformanceDisplayName = "高性能";
  public const string BalancedDisplayName = "平衡";
  public const string PowerSaverDisplayName = "节能";

  public static string GetConfigTierDisplayName(
    Guid planGuid,
    Guid activePlanGuid,
    Guid balancedPlanGuid,
    Guid powerSaverPlanGuid)
  {
    if (planGuid == activePlanGuid) return HighPerformanceDisplayName;
    if (planGuid == balancedPlanGuid) return BalancedDisplayName;
    if (planGuid == powerSaverPlanGuid) return PowerSaverDisplayName;
    return planGuid.ToString();
  }

  private static readonly Guid[] KnownSchemeGuids =
  [
    HighPerformancePlanGuid,
    BalancedPlanGuid,
    PowerSaverPlanGuid,
  ];

  private static readonly SemaphoreSlim AsyncLoadGate = new(1, 1);
  private static IReadOnlyDictionary<Guid, string>? _sessionCache;

  internal static Func<IReadOnlyDictionary<Guid, string>>? LoadImplementationForTests;

  internal static bool IsKnownScheme(Guid plan) => KnownSchemeGuids.Contains(plan);

  private static volatile PowerPlanCatalogSnapshot _snapshot = PowerPlanCatalogSnapshot.Loading;

  /// <summary>UI-safe: never performs IO. The latest load's result; loads run on background tasks.</summary>
  internal static PowerPlanCatalogSnapshot Snapshot => _snapshot;

  private sealed record CatalogLoad(bool Enumerated, IReadOnlyDictionary<Guid, string> Names,
    IReadOnlySet<Guid> Listed, IReadOnlySet<Guid> ConfirmedAbsent, bool Complete);

  // Test barrier: completes once no load holds the load gate. Every Settings load attempt, including its
  // snapshot publish, runs inside the gate, so a completed barrier means every earlier load has published.
  internal static async Task WhenLoadsIdleForTests()
  {
    await AsyncLoadGate.WaitAsync().ConfigureAwait(false);
    AsyncLoadGate.Release();
  }

  public static IReadOnlyDictionary<Guid, string> TryLoad()
  {
    if (_sessionCache is not null)
      return _sessionCache;

    try
    {
      var catalog = LoadImplementationForTests?.Invoke() ?? LoadCore();
      if (catalog.Count > 0 && !HasMissingKnownSchemes(catalog))
        _sessionCache = catalog;
      return catalog;
    }
    catch
    {
      return new Dictionary<Guid, string>();
    }
  }

  public static Task<IReadOnlyDictionary<Guid, string>> LoadAsync(CancellationToken cancellationToken = default)
    => LoadWithRetryAsync(cancellationToken: cancellationToken);

  internal static readonly TimeSpan LoadBudget = TimeSpan.FromSeconds(5);

  // Guardian-only entry (PWR-INV-SCHEME-API).
  internal static async Task<IReadOnlyDictionary<Guid, string>> TryLoadOnceAsync(CancellationToken cancellationToken,
    Action<PowerSchemeStatus>? observe = null, TimeSpan? budget = null)
  {
    if (_sessionCache is not null) return _sessionCache;
    if (LoadImplementationForTests is { } loader) return loader();
    var load = await LoadThroughSchemeServiceAsync(cancellationToken, observe, budget ?? LoadBudget).ConfigureAwait(false);
    Publish(load);
    return load.Names;
  }

  // One load through PowerSchemeService: enumeration and every name read share one total budget, so the
  // chain is bounded regardless of the scheme count. Names are presentation only; only a confirmed
  // absence (Win32 2) of a hidden known scheme is normal.
  private static async Task<CatalogLoad> LoadThroughSchemeServiceAsync(
    CancellationToken cancellationToken, Action<PowerSchemeStatus>? observe, TimeSpan budget)
  {
    var names = new Dictionary<Guid, string>();
    var absent = new HashSet<Guid>();
    using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    total.CancelAfter(budget);
    var started = Stopwatch.GetTimestamp();
    HashSet<Guid>? listed = null;
    var complete = false;
    try
    {
      var schemes = await PowerSchemeService.EnumerateSchemesAsync(total.Token).ConfigureAwait(false);
      observe?.Invoke(schemes.Status);
      if (!schemes.Succeeded) return new(false, names, new HashSet<Guid>(), absent, false);

      var enumerated = schemes.Value!.ToHashSet();
      listed = enumerated;
      complete = true;
      foreach (var guid in enumerated.Concat(KnownSchemeGuids.Where(known => !enumerated.Contains(known))))
      {
        var name = await PowerSchemeService.ReadNameAsync(guid, total.Token).ConfigureAwait(false);
        if (name.Succeeded)
        {
          if (!string.IsNullOrWhiteSpace(name.Value)) names[guid] = name.Value!;
          continue;
        }
        if (!enumerated.Contains(guid) && name.Status is { FailureKind: "api-error", Win32Error: 2 })
        {
          absent.Add(guid);
          continue;
        }
        complete = false;
        observe?.Invoke(name.Status);
      }
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      // Total budget exhausted: keep the names read so far; the guardian continues by GUID.
      complete = false;
      observe?.Invoke(new PowerSchemeStatus("timed-out", null,
        (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds));
    }
    // Enumerated even when the budget ran out during name reads: unread schemes stay NameUnreadable.
    return new(listed is not null, names, listed ?? new HashSet<Guid>(), absent, complete);
  }

  // Test loaders return the machine's complete list; built-in schemes they omit count as confirmed absent.
  private static CatalogLoad FromTestLoader(IReadOnlyDictionary<Guid, string> catalog)
    => new(true, catalog, catalog.Keys.ToHashSet(), KnownSchemeGuids.Where(g => !catalog.ContainsKey(g)).ToHashSet(), true);

  private static void Publish(CatalogLoad load)
  {
    if (load.Enumerated)
      _snapshot = new PowerPlanCatalogSnapshot(PowerPlanCatalogState.Loaded, load.Names, load.Listed, load.ConfirmedAbsent);
    if (load.Complete && load.Names.Count > 0 && !HasMissingKnownSchemes(load.Names)) _sessionCache = load.Names;
  }

  private static void PublishFailureUnlessLoaded()
  {
    if (_snapshot.State != PowerPlanCatalogState.Loaded) _snapshot = PowerPlanCatalogSnapshot.Failed;
  }

  // One Settings attempt: same core as the guardian; a failing test loader counts as a failed enumeration.
  private static async Task<CatalogLoad> LoadAttemptAsync(CancellationToken cancellationToken)
  {
    if (_sessionCache is { } cached) return FromTestLoader(cached);
    CatalogLoad load;
    if (LoadImplementationForTests is { } loader)
    {
      try { load = FromTestLoader(await Task.Run(loader, cancellationToken).ConfigureAwait(false)); }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
      catch { load = new(false, new Dictionary<Guid, string>(), new HashSet<Guid>(), new HashSet<Guid>(), false); }
    }
    else
    {
      load = await LoadThroughSchemeServiceAsync(cancellationToken, null, LoadBudget).ConfigureAwait(false);
    }
    cancellationToken.ThrowIfCancellationRequested();
    Publish(load);
    return load;
  }

  public static async Task<IReadOnlyDictionary<Guid, string>> LoadWithRetryAsync(
    int maxAttempts = 3,
    CancellationToken cancellationToken = default)
  {
    if (maxAttempts <= 0)
      throw new ArgumentOutOfRangeException(nameof(maxAttempts));

    cancellationToken.ThrowIfCancellationRequested();
    var cached = _sessionCache;
    if (cached is not null && !HasMissingKnownSchemes(cached))
      return cached;

    await AsyncLoadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      cached = _sessionCache;
      if (cached is not null && !HasMissingKnownSchemes(cached))
        return cached;

      CatalogLoad? last = null;
      for (var attempt = 0; attempt < maxAttempts; attempt++)
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (attempt > 0)
          InvalidateSessionCache();
        last = await LoadAttemptAsync(cancellationToken).ConfigureAwait(false);
        if (last.Names.Count > 0 && !HasMissingKnownSchemes(last.Names))
          return last.Names;

        if (attempt < maxAttempts - 1)
          await Task.Delay(300, cancellationToken).ConfigureAwait(false);
      }

      // Existing semantics: one final reload after the attempts (maxAttempts + 1 loads per call).
      cancellationToken.ThrowIfCancellationRequested();
      InvalidateSessionCache();
      last = await LoadAttemptAsync(cancellationToken).ConfigureAwait(false);
      if (!last.Enumerated) PublishFailureUnlessLoaded();
      return last.Names;
    }
    finally
    {
      AsyncLoadGate.Release();
    }
  }

  public static IReadOnlyDictionary<Guid, string> LoadWithRetry(int maxAttempts = 3)
  {
    for (var attempt = 0; attempt < maxAttempts; attempt++)
    {
      InvalidateSessionCache();
      var catalog = TryLoad();
      if (catalog.Count > 0 && !HasMissingKnownSchemes(catalog))
        return catalog;

      if (attempt < maxAttempts - 1)
        Thread.Sleep(300);
    }

    InvalidateSessionCache();
    return TryLoad();
  }

  public static void InvalidateSessionCache() => _sessionCache = null;

  public static bool HasMissingKnownSchemes(IReadOnlyDictionary<Guid, string> catalog)
    => KnownSchemeGuids.Any(guid => !catalog.ContainsKey(guid));

  internal static void ClearSessionCacheForTests()
  {
    InvalidateSessionCache();
    LoadImplementationForTests = null;
    _snapshot = PowerPlanCatalogSnapshot.Loading;
  }

  public static void EnrichWithHiddenSchemes(
    IDictionary<Guid, string> catalog,
    Func<Guid, string?> queryPlanName)
  {
    foreach (var guid in KnownSchemeGuids)
    {
      if (catalog.ContainsKey(guid))
        continue;

      var name = queryPlanName(guid);
      if (!string.IsNullOrWhiteSpace(name))
        catalog[guid] = name!;
    }
  }

  private static IReadOnlyDictionary<Guid, string> LoadCore()
  {
    var listResult = PowerCfgProcessRunner.RunPowerCfg("/list", TimeSpan.FromSeconds(15));
    if (!listResult.Succeeded)
      return new Dictionary<Guid, string>();

    var catalog = PowerPlanCatalogParser.ParseList(listResult.StandardOutput);
    EnrichWithHiddenSchemes(catalog, TryQueryPlanName);

    if (HasMissingKnownSchemes(catalog))
    {
      Thread.Sleep(250);
      EnrichWithHiddenSchemes(catalog, TryQueryPlanName);
    }

    return catalog;
  }

  private static async Task<IReadOnlyDictionary<Guid, string>> TryLoadAsync(
    CancellationToken cancellationToken, Action<PowerCfgCommandResult>? observe = null)
  {
    if (_sessionCache is not null)
      return _sessionCache;

    try
    {
      var catalog = LoadImplementationForTests is { } loader
        ? await Task.Run(loader, cancellationToken).ConfigureAwait(false)
        : await LoadCoreAsync(cancellationToken, observe).ConfigureAwait(false);

      cancellationToken.ThrowIfCancellationRequested();
      if (catalog.Count > 0 && !HasMissingKnownSchemes(catalog))
        _sessionCache = catalog;
      return catalog;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch
    {
      return new Dictionary<Guid, string>();
    }
  }

  private static async Task<IReadOnlyDictionary<Guid, string>> LoadCoreAsync(
    CancellationToken cancellationToken, Action<PowerCfgCommandResult>? observe = null)
  {
    var listResult = await PowerCfgProcessRunner.RunPowerCfgAsync(
        "/list",
        cancellationToken,
        TimeSpan.FromSeconds(15))
      .ConfigureAwait(false);
    observe?.Invoke(listResult);
    cancellationToken.ThrowIfCancellationRequested();
    if (!listResult.Succeeded)
      return new Dictionary<Guid, string>();

    var catalog = PowerPlanCatalogParser.ParseList(listResult.StandardOutput);
    await EnrichWithHiddenSchemesAsync(catalog, cancellationToken, observe).ConfigureAwait(false);

    if (HasMissingKnownSchemes(catalog))
    {
      await Task.Delay(250, cancellationToken).ConfigureAwait(false);
      await EnrichWithHiddenSchemesAsync(catalog, cancellationToken, observe).ConfigureAwait(false);
    }

    return catalog;
  }

  private static async Task EnrichWithHiddenSchemesAsync(
    IDictionary<Guid, string> catalog,
    CancellationToken cancellationToken, Action<PowerCfgCommandResult>? observe = null)
  {
    foreach (var guid in KnownSchemeGuids)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (catalog.ContainsKey(guid))
        continue;

      var name = await TryQueryPlanNameAsync(guid, cancellationToken, observe).ConfigureAwait(false);
      if (!string.IsNullOrWhiteSpace(name))
        catalog[guid] = name!;
    }
  }

  private static PowerCfgCommandResult RunPowerCfg(string arguments)
    => PowerCfgProcessRunner.RunPowerCfg(arguments, TimeSpan.FromSeconds(15));

  private static string? TryQueryPlanName(Guid guid)
  {
    try
    {
      var result = PowerCfgProcessRunner.RunPowerCfg(
        $"/query {guid:D}",
        TimeSpan.FromSeconds(15));
      if (!result.Succeeded ||
          !PowerPlanCatalogParser.TryParseQueryHeader(result.StandardOutput, out var parsedGuid, out var name))
        return null;

      return parsedGuid == guid ? name : null;
    }
    catch
    {
      return null;
    }
  }

  private static async Task<string?> TryQueryPlanNameAsync(
    Guid guid,
    CancellationToken cancellationToken, Action<PowerCfgCommandResult>? observe = null)
  {
    try
    {
      var result = await PowerCfgProcessRunner.RunPowerCfgAsync(
          $"/query {guid:D}",
          cancellationToken,
          TimeSpan.FromSeconds(15))
        .ConfigureAwait(false);
      observe?.Invoke(result);
      cancellationToken.ThrowIfCancellationRequested();
      if (!result.Succeeded ||
          !PowerPlanCatalogParser.TryParseQueryHeader(result.StandardOutput, out var parsedGuid, out var name))
        return null;

      return parsedGuid == guid ? name : null;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch
    {
      return null;
    }
  }
}
