namespace SmartGuard.Configuration;

internal enum PowerSchemeSetting { VideoNormalLevel, AdaptiveBrightness }

internal readonly record struct PowerSchemeStatus(string FailureKind, int? Win32Error, long ElapsedMilliseconds)
{
  public bool Succeeded => FailureKind == "none";
}

internal readonly record struct PowerSchemeResult<T>(PowerSchemeStatus Status, T? Value)
{
  public bool Succeeded => Status.Succeeded;
}

/// <summary>
/// Single-flight executor (PWR-INV-SCHEME-API). The work item owns the gate and releases it in its own
/// finally, after the native function returned and before the result becomes visible to the caller, so a
/// completed call never refuses its caller's next call. A call past its deadline keeps the gate until the
/// native function really returns; meanwhile new calls are refused as call-pending. A work item that has
/// not entered the native layer skips it when its caller gave up (deadline or cancellation) or when a
/// write is no longer admitted. The caller never releases the gate.
/// </summary>
internal sealed class PowerSchemeExecutor
{
  private const int Queued = 0, Entered = 1, Abandoned = 2;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private long _inFlightSince;
  private long _lastSucceededAt;

  internal PowerSchemeExecutor(IPowrProfNative native, TimeSpan? deadlineOverride = null, TaskScheduler? scheduler = null)
  {
    Native = native;
    DeadlineOverride = deadlineOverride;
    Scheduler = scheduler ?? TaskScheduler.Default;
  }

  internal IPowrProfNative Native { get; }
  internal TimeSpan? DeadlineOverride { get; }
  internal TaskScheduler Scheduler { get; }
  internal bool IsCallInFlight => Volatile.Read(ref _inFlightSince) != 0;
  internal long? InFlightSince => Volatile.Read(ref _inFlightSince) is var at && at != 0 ? at : (long?)null;
  internal long? LastSucceededAt => Volatile.Read(ref _lastSucceededAt) is var at && at != 0 ? at : (long?)null;

  private enum Outcome { Completed, Faulted, Skipped, NotAdmitted }

  private sealed class Ticket
  {
    public int State;
  }

  internal async Task<PowerSchemeResult<T>> RunAsync<T>(Func<IPowrProfNative, (uint Rc, T Value)> operation,
    TimeSpan deadline, Func<bool>? admit, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    if (!_gate.Wait(0)) return Failed<T>("call-pending", null, started);
    Volatile.Write(ref _inFlightSince, started);
    var ticket = new Ticket();
    Task<(Outcome Outcome, uint Rc, T? Value)> call;
    try
    {
      // No token on StartNew: a task cancelled before it starts would skip the finally that owns the release.
      call = Task.Factory.StartNew(() =>
      {
        try
        {
          // Linearization point: entering wins against a caller that has not yet given up, and vice versa.
          if (Interlocked.CompareExchange(ref ticket.State, Entered, Queued) != Queued
              || cancellationToken.IsCancellationRequested)
            return (Outcome.Skipped, 0u, default(T));
          if (admit?.Invoke() == false) return (Outcome.NotAdmitted, 0u, default(T));
          var (rc, value) = operation(Native);
          return (Outcome.Completed, rc, value);
        }
        catch (Exception)
        {
          // Observed here so a late fault after the caller left never becomes an unobserved task exception.
          return (Outcome.Faulted, 0u, default(T));
        }
        finally
        {
          Release();
        }
      }, CancellationToken.None, TaskCreationOptions.DenyChildAttach, Scheduler);
    }
    catch
    {
      Release();
      throw;
    }

    try
    {
      var (outcome, rc, value) = await call.WaitAsync(DeadlineOverride ?? deadline, cancellationToken).ConfigureAwait(false);
      switch (outcome)
      {
        case Outcome.Skipped:
          cancellationToken.ThrowIfCancellationRequested();
          return Failed<T>("timed-out", null, started);
        case Outcome.NotAdmitted:
          return Failed<T>("not-admitted", null, started);
        case Outcome.Faulted:
          return Failed<T>("api-error", null, started);
      }
      if (rc != 0) return Failed<T>("api-error", unchecked((int)rc), started);
      Volatile.Write(ref _lastSucceededAt, System.Diagnostics.Stopwatch.GetTimestamp());
      return new(new("none", null, Elapsed(started)), value);
    }
    catch (TimeoutException)
    {
      _ = Interlocked.CompareExchange(ref ticket.State, Abandoned, Queued);
      return Failed<T>("timed-out", null, started);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      _ = Interlocked.CompareExchange(ref ticket.State, Abandoned, Queued);
      throw;
    }
  }

  private void Release()
  {
    Volatile.Write(ref _inFlightSince, 0);
    _gate.Release();
  }

  private static PowerSchemeResult<T> Failed<T>(string kind, int? win32Error, long started)
    => new(new(kind, win32Error, Elapsed(started)), default);

  private static long Elapsed(long started) => (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}

/// <summary>
/// Sole entry for power scheme operations (PWR-INV-SCHEME-API). The real native layer has exactly one
/// executor per process; tests substitute their own executor for their async flow only. No fallback to
/// external processes.
/// </summary>
internal static class PowerSchemeService
{
  internal static readonly Guid VideoSubgroupGuid = new("7516b95f-f776-4464-8c53-06167f40cc99");
  internal static readonly Guid VideoNormalLevelGuid = new("aded5e82-b909-4619-9949-f5d71dac0bcb");
  internal static readonly Guid AdaptiveBrightnessGuid = new("fbd9aa66-9553-4097-ba44-ed6e9d65eab8");
  internal static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(2);
  internal static readonly TimeSpan EnumerateDeadline = TimeSpan.FromSeconds(5);

  private static readonly PowerSchemeExecutor RealExecutor = new(PowrProfNative.Instance);
  private static readonly AsyncLocal<PowerSchemeExecutor?> TestExecutor = new();

  /// <summary>Test assemblies clear this in a module initializer; real writes then throw before the native layer.</summary>
  internal static volatile bool RealWritesAllowed = true;

  internal static void UseExecutorForTests(PowerSchemeExecutor? executor) => TestExecutor.Value = executor;

  private static PowerSchemeExecutor Current => TestExecutor.Value ?? RealExecutor;

  private static PowerSchemeExecutor WriteExecutor()
  {
    var executor = Current;
    if (ReferenceEquals(executor, RealExecutor) && !RealWritesAllowed)
      throw new InvalidOperationException("Real power scheme writes are disabled in this process.");
    return executor;
  }

  internal static bool IsCallInFlight => Current.IsCallInFlight;
  internal static long? InFlightSince => Current.InFlightSince;
  internal static long? LastSucceededAt => Current.LastSucceededAt;

  internal static Task<PowerSchemeResult<Guid>> GetActiveAsync(CancellationToken cancellationToken = default)
    => Current.RunAsync(native => (native.GetActiveScheme(out var scheme), scheme), DefaultDeadline, null, cancellationToken);

  internal static Task<PowerSchemeResult<bool>> SetActiveAsync(Guid scheme, Func<bool>? admit = null,
    CancellationToken cancellationToken = default)
    => WriteExecutor().RunAsync(native => (native.SetActiveScheme(scheme), true), DefaultDeadline, admit, cancellationToken);

  internal static Task<PowerSchemeResult<string>> ReadNameAsync(Guid scheme, CancellationToken cancellationToken = default)
    => Current.RunAsync(native => (native.ReadFriendlyName(scheme, out var name), name), DefaultDeadline, null, cancellationToken);

  internal static Task<PowerSchemeResult<IReadOnlyList<Guid>>> EnumerateSchemesAsync(CancellationToken cancellationToken = default)
    => Current.RunAsync<IReadOnlyList<Guid>>(native => (native.EnumerateSchemes(out var schemes), schemes),
      EnumerateDeadline, null, cancellationToken);

  internal static Task<PowerSchemeResult<uint>> ReadValueIndexAsync(Guid scheme, PowerSchemeSetting setting, PowerSource source,
    CancellationToken cancellationToken = default)
    => Current.RunAsync(native => (native.ReadValueIndex(scheme, VideoSubgroupGuid, SettingGuid(setting), source, out var value), value),
      DefaultDeadline, null, cancellationToken);

  internal static Task<PowerSchemeResult<bool>> WriteValueIndexAsync(Guid scheme, PowerSchemeSetting setting, PowerSource source,
    uint value, Func<bool>? admit = null, CancellationToken cancellationToken = default)
    => WriteExecutor().RunAsync(native => (native.WriteValueIndex(scheme, VideoSubgroupGuid, SettingGuid(setting), source, value), true),
      DefaultDeadline, admit, cancellationToken);

  private static Guid SettingGuid(PowerSchemeSetting setting) => setting switch
  {
    PowerSchemeSetting.VideoNormalLevel => VideoNormalLevelGuid,
    PowerSchemeSetting.AdaptiveBrightness => AdaptiveBrightnessGuid,
    _ => throw new ArgumentOutOfRangeException(nameof(setting)),
  };
}
