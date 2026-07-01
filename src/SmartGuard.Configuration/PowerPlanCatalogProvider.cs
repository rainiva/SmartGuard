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

  private static IReadOnlyDictionary<Guid, string>? _sessionCache;

  internal static Func<IReadOnlyDictionary<Guid, string>>? LoadImplementationForTests;

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
    => Task.Run(() =>
    {
      cancellationToken.ThrowIfCancellationRequested();
      return TryLoad();
    });

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
    var catalog = PowerPlanCatalogParser.ParseList(
      PowerCfgProcessRunner.RunPowerCfg("/list", TimeSpan.FromSeconds(15)));
    EnrichWithHiddenSchemes(catalog, TryQueryPlanName);

    if (HasMissingKnownSchemes(catalog))
    {
      Thread.Sleep(250);
      EnrichWithHiddenSchemes(catalog, TryQueryPlanName);
    }

    return catalog;
  }

  private static string RunPowerCfg(string arguments)
    => PowerCfgProcessRunner.RunPowerCfg(arguments, TimeSpan.FromSeconds(15));

  private static string? TryQueryPlanName(Guid guid)
  {
    try
    {
      var output = PowerCfgProcessRunner.RunPowerCfg(
        $"/query {guid:D}",
        TimeSpan.FromSeconds(15));
      if (!PowerPlanCatalogParser.TryParseQueryHeader(output, out var parsedGuid, out var name))
        return null;

      return parsedGuid == guid ? name : null;
    }
    catch
    {
      return null;
    }
  }
}
