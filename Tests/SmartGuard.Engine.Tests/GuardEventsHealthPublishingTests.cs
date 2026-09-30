using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

/// <summary>
/// 任务 4：前台事件监听健康发布测试。
/// 限制开启时 Runner 经注入接缝读取监听健康并发布 foregroundEventsAvailable
/// （true=健康 / false=降级为周期补偿）；限制关闭或配置不可用时不发布（null）。
/// </summary>
public class GuardEventsHealthPublishingTests : IDisposable
{
  private static readonly string ListedProgram = @"C:\Windows\System32\notepad.exe";

  private readonly string _root = Path.Combine(
    Path.GetTempPath(), "SmartGuardGuardEventsHealth_" + Guid.NewGuid().ToString("N"));
  private readonly ManualTimeProvider _time = new();

  public GuardEventsHealthPublishingTests()
  {
    Directory.CreateDirectory(_root);
  }

  public void Dispose()
  {
    try { Directory.Delete(_root, recursive: true); } catch { }
  }

  [Fact]
  public void Run_reports_healthy_foreground_events_when_restriction_enabled()
  {
    var harness = Create(eventsHealth: true, restrictionEnabled: true);
    harness.Run();

    harness.ReadStatus().foregroundEventsAvailable.Should().BeTrue();
  }

  [Fact]
  public void Run_reports_degraded_foreground_events_when_listener_is_unhealthy()
  {
    var harness = Create(eventsHealth: false, restrictionEnabled: true);
    harness.Run();

    harness.ReadStatus().foregroundEventsAvailable.Should().BeFalse(
      "监听降级 → 周期补偿，状态必须可观测");
  }

  [Fact]
  public void Run_omits_foreground_events_availability_when_restriction_disabled()
  {
    var harness = Create(eventsHealth: true, restrictionEnabled: false);
    harness.Run();

    harness.ReadRawStatus().Should().NotContain("foregroundEventsAvailable",
      "限制关闭时事件监听不适用，null 字段不得写盘");
  }

  [Fact]
  public void Run_omits_foreground_events_availability_when_config_unavailable()
  {
    var harness = Create(eventsHealth: true, restrictionEnabled: true);
    harness.ConfigAvailable = false;
    harness.Run();

    harness.ReadStatus().foregroundEventsAvailable.Should().BeNull("配置不可用时监听健康未知，不得猜测");
  }

  private GuardEventsHealthHarness Create(bool eventsHealth, bool restrictionEnabled)
  {
    var harness = new GuardEventsHealthHarness(_root, Path.Combine(_root, "SmartGuard.status.json"), _time)
    {
      EventsHealth = eventsHealth,
    };
    harness.Config.ForegroundRestrictionEnabled = restrictionEnabled;
    if (restrictionEnabled)
      harness.Config.ForegroundProgramPaths = [ListedProgram];
    return harness;
  }

  private sealed class GuardEventsHealthHarness : IDisposable
  {
    private readonly ManualTimeProvider _time;
    private readonly string _statusPath;
    private readonly GuardianLoopIterationState _state = new();

    public GuardConfig Config { get; }
    public bool ConfigAvailable { get; set; } = true;
    public bool? EventsHealth { get; set; }

    public GuardEventsHealthHarness(string root, string statusPath, ManualTimeProvider time)
    {
      _statusPath = statusPath;
      _time = time;
      Config = GuardConfig.CreateDefault(root);
      Config.BrightnessRestoreMs = 0;
      Config.HeartbeatIntervalMin = 0;
      _state.PlanCatalog = new Dictionary<Guid, string>
      {
        [PowerPlanCatalogProvider.HighPerformancePlanGuid] = PowerPlanCatalogProvider.HighPerformanceDisplayName,
        [PowerPlanCatalogProvider.BalancedPlanGuid] = PowerPlanCatalogProvider.BalancedDisplayName,
        [PowerPlanCatalogProvider.PowerSaverPlanGuid] = PowerPlanCatalogProvider.PowerSaverDisplayName,
      };
      _state.LastKnownGuid = PowerPlanCatalogProvider.HighPerformancePlanGuid;
      Runner = new GuardianIterationRunner(
        new IdleTracker(time),
        new BrightnessService(),
        new StatusPublisher(statusPath, time),
        () => ConfigAvailable
          ? GuardConfigReadResult.FromConfig(Config)
          : GuardConfigReadResult.Unavailable,
        (_, _, _, _) => GuardianLogWriteResult.AcceptedByPipeline,
        _ => { },
        powerCfgBrightnessSupported: false,
        heartbeatSchedule: new HeartbeatSchedule(time),
        readIdleSeconds: () => 10,
        readBatteryInfo: _ => (80, true),
        readCurrentPlan: () => new PowerPlanProbeResult(
          true,
          new PowerSchemeInfo(PowerPlanCatalogProvider.HighPerformancePlanGuid, null),
          PowerPlanFailureKind.None,
          string.Empty),
        applyPlan: (target, _) => Task.FromResult(new PowerPlanApplyResult(
          true, target, target, PowerPlanFailureKind.None, string.Empty)),
        timeProvider: time,
        readForeground: () => ForegroundObservation.Identified(ListedProgram),
        readForegroundEventsHealth: () => EventsHealth);
    }

    public GuardianIterationRunner Runner { get; }

    public void Run()
      => Runner.RunAsync(
        _state,
        GuardianIterationContext.Running,
        CancellationToken.None).GetAwaiter().GetResult();

    public string ReadRawStatus() => File.ReadAllText(_statusPath);

    public SmartGuard.Contracts.StatusPayload ReadStatus()
      => StatusJsonReader.TryRead(_statusPath) ?? throw new InvalidOperationException("status.json 未发布");

    public void Dispose()
    {
    }
  }
}
