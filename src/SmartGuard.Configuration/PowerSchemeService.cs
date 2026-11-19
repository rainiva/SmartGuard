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

/// <summary>Scaffold only; execution semantics arrive with their tests.</summary>
internal sealed class PowerSchemeExecutor
{
  internal PowerSchemeExecutor(IPowrProfNative native, TimeSpan? deadlineOverride = null, TaskScheduler? scheduler = null)
  {
    Native = native;
    DeadlineOverride = deadlineOverride;
    Scheduler = scheduler ?? TaskScheduler.Default;
  }

  internal IPowrProfNative Native { get; }
  internal TimeSpan? DeadlineOverride { get; }
  internal TaskScheduler Scheduler { get; }
  internal bool IsCallInFlight => false;
  internal long? InFlightSince => null;
  internal long? LastSucceededAt => null;

  internal Task<PowerSchemeResult<T>> RunAsync<T>(Func<IPowrProfNative, (uint Rc, T Value)> operation, TimeSpan deadline,
    Func<bool>? admit, CancellationToken cancellationToken)
    => Task.FromResult(new PowerSchemeResult<T>(new("api-error", null, 0), default));
}

/// <summary>Scaffold only; execution semantics arrive with their tests.</summary>
internal static class PowerSchemeService
{
  internal static readonly Guid VideoSubgroupGuid = new("7516b95f-f776-4464-8c53-06167f40cc99");
  internal static readonly Guid VideoNormalLevelGuid = new("aded5e82-b909-4619-9949-f5d71dac0bcb");
  internal static readonly Guid AdaptiveBrightnessGuid = new("fbd9aa66-9553-4097-ba44-ed6e9d65eab8");
  internal static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(2);
  internal static readonly TimeSpan EnumerateDeadline = TimeSpan.FromSeconds(5);

  internal static volatile bool RealWritesAllowed = true;
  internal static void UseExecutorForTests(PowerSchemeExecutor? executor) { }

  internal static bool IsCallInFlight => false;
  internal static long? InFlightSince => null;
  internal static long? LastSucceededAt => null;

  internal static Task<PowerSchemeResult<Guid>> GetActiveAsync(CancellationToken cancellationToken = default) => Stub<Guid>();
  internal static Task<PowerSchemeResult<bool>> SetActiveAsync(Guid scheme, Func<bool>? admit = null,
    CancellationToken cancellationToken = default) => Stub<bool>();
  internal static Task<PowerSchemeResult<string>> ReadNameAsync(Guid scheme, CancellationToken cancellationToken = default) => Stub<string>();
  internal static Task<PowerSchemeResult<IReadOnlyList<Guid>>> EnumerateSchemesAsync(CancellationToken cancellationToken = default) => Stub<IReadOnlyList<Guid>>();
  internal static Task<PowerSchemeResult<uint>> ReadValueIndexAsync(Guid scheme, PowerSchemeSetting setting, PowerSource source,
    CancellationToken cancellationToken = default) => Stub<uint>();
  internal static Task<PowerSchemeResult<bool>> WriteValueIndexAsync(Guid scheme, PowerSchemeSetting setting, PowerSource source,
    uint value, Func<bool>? admit = null, CancellationToken cancellationToken = default) => Stub<bool>();

  private static Task<PowerSchemeResult<T>> Stub<T>() => Task.FromResult(new PowerSchemeResult<T>(new("api-error", null, 0), default));
}
