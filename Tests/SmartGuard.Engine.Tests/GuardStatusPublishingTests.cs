using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

/// <summary>
/// 任务 4：守护决策原因发布链行为测试。
/// Runner 每轮把统一决策（guardSource/guardReason）与前台事件监听健康发布进 status.json；
/// 降级/探测失败/生命周期门禁路径有独立稳定代码；代码取值限
/// <see cref="GuardStatusCodes"/> 登记表；Publisher 相等判定覆盖新字段。
/// </summary>
public class GuardStatusPublishingTests : IDisposable
{
  private static readonly string ListedProgram = @"C:\Windows\System32\notepad.exe";

  private readonly string _root;

  public GuardStatusPublishingTests()
  {
    _root = Path.Combine(Path.GetTempPath(), "SmartGuardGuardStatus_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_root);
  }

  public void Dispose()
  {
    try { Directory.Delete(_root, recursive: true); } catch { }
  }

  // ---- StatusPublisher 相等判定：只有新字段变化也必须写盘 ----

  [Fact]
  public void Publish_writes_when_only_guard_reason_changes()
  {
    var publisher = new StatusPublisher(StatusPath, ManualTime);
    publisher.Publish(Payload(reason: GuardStatusCodes.Reasons.PolicySelected));
    var firstJson = File.ReadAllText(StatusPath);

    publisher.Publish(Payload(reason: GuardStatusCodes.Reasons.WaitingForListedProgram));

    File.ReadAllText(StatusPath).Should().NotBe(firstJson,
      "目标计划等其他字段不变、只有 guardReason 改变时必须写入 status.json，否则原因变化永远到不了 UI");
  }

  [Fact]
  public void Publish_writes_when_only_guard_source_changes()
  {
    var publisher = new StatusPublisher(StatusPath, ManualTime);
    publisher.Publish(Payload(source: GuardStatusCodes.Sources.None));
    var firstJson = File.ReadAllText(StatusPath);

    publisher.Publish(Payload(source: GuardStatusCodes.Sources.Automatic));

    File.ReadAllText(StatusPath).Should().NotBe(firstJson);
  }

  [Fact]
  public void Publish_writes_when_only_foreground_events_availability_changes()
  {
    var publisher = new StatusPublisher(StatusPath, ManualTime);
    publisher.Publish(Payload(eventsAvailable: true));
    var firstJson = File.ReadAllText(StatusPath);

    publisher.Publish(Payload(eventsAvailable: false));

    File.ReadAllText(StatusPath).Should().NotBe(firstJson,
      "监听降级（周期补偿）必须能写进状态，UI 才能区分事件通知与周期采样");
  }

  [Fact]
  public void Publish_skips_write_when_guard_fields_unchanged()
  {
    var publisher = new StatusPublisher(StatusPath, ManualTime);
    publisher.Publish(Payload(reason: GuardStatusCodes.Reasons.ForegroundMatched));
    var firstJson = File.ReadAllText(StatusPath);

    publisher.Publish(Payload(reason: GuardStatusCodes.Reasons.ForegroundMatched));

    File.ReadAllText(StatusPath).Should().Be(firstJson, "守护字段无变化仍按既有去重不写盘");
  }

  // ---- Runner 原因传播：每个统一决策原因都发布登记的稳定代码 ----

  [Fact]
  public void Run_publishes_policy_selected_reason_when_restriction_disabled()
  {
    using var harness = CreateHarness(h => h.Config.ForegroundRestrictionEnabled = false);

    harness.Run();

    var status = harness.ReadStatus();
    status.guardSource.Should().Be(GuardStatusCodes.Sources.Automatic);
    status.guardReason.Should().Be(GuardStatusCodes.Reasons.PolicySelected);
  }

  [Fact]
  public void Run_publishes_foreground_matched_reason_when_listed_program_is_foreground()
  {
    using var harness = CreateHarness(h =>
    {
      h.Config.ForegroundRestrictionEnabled = true;
      h.Config.ForegroundProgramPaths = [ListedProgram];
      h.ForegroundPath = ListedProgram;
    });

    harness.Run();

    var status = harness.ReadStatus();
    status.guardSource.Should().Be(GuardStatusCodes.Sources.Automatic);
    status.guardReason.Should().Be(GuardStatusCodes.Reasons.ForegroundMatched);
  }

  [Fact]
  public void Run_publishes_manual_active_reason_during_manual_takeover()
  {
    using var harness = CreateHarness(h =>
    {
      h.Config.ForegroundRestrictionEnabled = true;
      h.Config.ForegroundProgramPaths = [ListedProgram];
      h.ForegroundPath = @"C:\Windows\System32\calc.exe";
      h.Config.ManualHighPerformanceUntil = DateTime.Now.AddMinutes(10);
    });

    harness.Run();

    var status = harness.ReadStatus();
    status.guardSource.Should().Be(GuardStatusCodes.Sources.Manual);
    status.guardReason.Should().Be(GuardStatusCodes.Reasons.ManualActive);
  }

  [Fact]
  public void Run_publishes_paused_reason_without_converting_it_to_hold_display()
  {
    using var harness = CreateHarness(h =>
    {
      h.Config.ForegroundRestrictionEnabled = true;
      h.Config.ForegroundProgramPaths = [ListedProgram];
      h.Config.Paused = true;
    });

    harness.Run();

    var status = harness.ReadStatus();
    status.guardSource.Should().Be(GuardStatusCodes.Sources.None);
    status.guardReason.Should().Be(GuardStatusCodes.Reasons.Paused);
    status.paused.Should().BeTrue("暂停时 paused 字段语义保持不变");
  }

  [Fact]
  public void Run_publishes_foreground_list_empty_reason()
  {
    using var harness = CreateHarness(h =>
    {
      h.Config.ForegroundRestrictionEnabled = true;
      h.Config.ForegroundProgramPaths = [];
    });

    harness.Run();

    var status = harness.ReadStatus();
    status.guardSource.Should().Be(GuardStatusCodes.Sources.None);
    status.guardReason.Should().Be(GuardStatusCodes.Reasons.ForegroundListEmpty);
  }

  [Fact]
  public void Run_publishes_foreground_identity_unknown_reason()
  {
    using var harness = CreateHarness(h =>
    {
      h.Config.ForegroundRestrictionEnabled = true;
      h.Config.ForegroundProgramPaths = [ListedProgram];
      h.ForegroundPath = null;
    });

    harness.Run();

    var status = harness.ReadStatus();
    status.guardSource.Should().Be(GuardStatusCodes.Sources.None);
    status.guardReason.Should().Be(GuardStatusCodes.Reasons.ForegroundIdentityUnknown);
  }

  [Fact]
  public void Run_publishes_waiting_for_listed_program_reason()
  {
    using var harness = CreateHarness(h =>
    {
      h.Config.ForegroundRestrictionEnabled = true;
      h.Config.ForegroundProgramPaths = [ListedProgram];
      h.ForegroundPath = @"C:\Windows\System32\calc.exe";
    });

    harness.Run();

    var status = harness.ReadStatus();
    status.guardSource.Should().Be(GuardStatusCodes.Sources.None);
    status.guardReason.Should().Be(GuardStatusCodes.Reasons.WaitingForListedProgram);
    status.paused.Should().BeFalse("正常未命中不得写成 Paused=true");
  }

  [Fact]
  public void Run_publishes_foreground_program_paths_invalid_reason()
  {
    using var harness = CreateHarness(h =>
    {
      h.Config.ForegroundRestrictionEnabled = true;
      h.Config.ForegroundProgramPaths = ["not-an-absolute-path.exe"];
    });

    harness.Run();

    var status = harness.ReadStatus();
    status.guardSource.Should().Be(GuardStatusCodes.Sources.None);
    status.guardReason.Should().Be(GuardStatusCodes.Reasons.ForegroundProgramPathsInvalid);
  }

  // ---- 执行层降级路径 ----

  [Fact]
  public void Run_publishes_config_unavailable_reason_when_config_read_is_invalid()
  {
    using var harness = CreateHarness(h => h.ConfigAvailable = false);

    harness.Run();

    var status = harness.ReadStatus();
    status.guardSource.Should().Be(GuardStatusCodes.Sources.None);
    status.guardReason.Should().Be(GuardStatusCodes.Reasons.ConfigUnavailable);
    status.expectedPlan.Should().BeNull();
    status.foregroundEventsAvailable.Should().BeNull("配置不可用时事件监听健康未知，不得猜测");
  }

  [Fact]
  public void Run_publishes_probe_failed_reason_when_plan_probe_fails()
  {
    using var harness = CreateHarness(h =>
    {
      h.Config.ForegroundRestrictionEnabled = true;
      h.Config.ForegroundProgramPaths = [ListedProgram];
      h.FailPlanProbe = true;
    });

    harness.Run();

    var status = harness.ReadStatus();
    status.guardReason.Should().Be(GuardStatusCodes.Reasons.ProbeFailed,
      "探测失败也必须发布状态，否则 UI 停留在旧原因无法解释守护停摆");
  }

  [Fact]
  public void Run_publishes_blocked_by_lifecycle_reason_when_mutation_is_not_allowed()
  {
    using var harness = CreateHarness(h =>
    {
      h.Config.ForegroundRestrictionEnabled = false;
      h.AllowPolicyMutation = false;
    });

    harness.Run();

    var status = harness.ReadStatus();
    status.guardReason.Should().Be(GuardStatusCodes.Reasons.BlockedByLifecycle,
      "电源生命周期门禁阻止变更时，不得把决策显示为普通策略守护");
  }

  // ---- 前台事件监听健康 ----

  [Fact]
  public void Run_omits_foreground_events_availability_when_restriction_disabled()
  {
    using var harness = CreateHarness(h =>
    {
      h.Config.ForegroundRestrictionEnabled = false;
    });

    harness.Run();

    harness.ReadRawStatus().Should().NotContain("foregroundEventsAvailable",
      "限制关闭时事件监听不适用，null 字段不得写盘");
  }

  // ---- 代码登记完整性 ----

  [Fact]
  public void Guard_decision_codes_cover_all_decision_values_and_are_registered()
  {
    foreach (GuardDecisionSource source in Enum.GetValues<GuardDecisionSource>())
    {
      var code = GuardDecisionCodes.ToSourceCode(source);
      GuardStatusCodes.IsKnownSource(code).Should().BeTrue($"来源 {source} 必须登记稳定代码（实际：{code}）");
    }

    foreach (GuardDecisionReason reason in Enum.GetValues<GuardDecisionReason>())
    {
      var code = GuardDecisionCodes.ToReasonCode(reason);
      GuardStatusCodes.IsKnownReason(code).Should().BeTrue($"原因 {reason} 必须登记稳定代码（实际：{code}）");
    }
  }

  [Fact]
  public void Status_registry_covers_all_registered_reason_codes()
  {
    var expected = new[]
    {
      GuardStatusCodes.Reasons.ManualActive,
      GuardStatusCodes.Reasons.PolicySelected,
      GuardStatusCodes.Reasons.ForegroundMatched,
      GuardStatusCodes.Reasons.Paused,
      GuardStatusCodes.Reasons.ForegroundListEmpty,
      GuardStatusCodes.Reasons.ForegroundIdentityUnknown,
      GuardStatusCodes.Reasons.WaitingForListedProgram,
      GuardStatusCodes.Reasons.ForegroundProgramPathsInvalid,
      GuardStatusCodes.Reasons.ConfigUnavailable,
      GuardStatusCodes.Reasons.BlockedByLifecycle,
      GuardStatusCodes.Reasons.ProbeFailed,
    };

    expected.Should().OnlyHaveUniqueItems();
    expected.Should().OnlyContain(code => GuardStatusCodes.IsKnownReason(code));
    GuardStatusCodes.IsKnownReason("not-a-registered-code").Should().BeFalse();
    GuardStatusCodes.IsKnownSource("not-a-registered-source").Should().BeFalse();
  }

  // ---- harness ----

  private ManualTimeProvider ManualTime { get; } = new();

  private string StatusPath => Path.Combine(_root, "SmartGuard.status.json");

  /// <summary>同一测试内共享计划 GUID，避免无关字段变化干扰相等性判定。</summary>
  private string PlanGuid { get; } = Guid.NewGuid().ToString();

  private StatusPayload Payload(
    string? source = GuardStatusCodes.Sources.Automatic,
    string? reason = GuardStatusCodes.Reasons.PolicySelected,
    bool? eventsAvailable = null)
    => new()
    {
      timestamp = DateTime.Now.ToString("s"),
      currentPlan = "平衡",
      currentPlanGUID = PlanGuid,
      expectedPlan = "平衡",
      idleSeconds = 10,
      isOnAC = true,
      batteryPercent = 80,
      brightness = 60,
      paused = false,
      guardSource = source,
      guardReason = reason,
      foregroundEventsAvailable = eventsAvailable,
    };

  private GuardStatusHarness CreateHarness(Action<GuardStatusHarness> configure)
  {
    var harness = new GuardStatusHarness(_root, StatusPath, ManualTime);
    configure(harness);
    return harness;
  }

  private sealed class GuardStatusHarness : IDisposable
  {
    private readonly ManualTimeProvider _time;
    private readonly GuardianLoopIterationState _state = new();

    public GuardConfig Config { get; }
    public bool ConfigAvailable { get; set; } = true;
    public string? ForegroundPath { get; set; }
    public bool FailPlanProbe { get; set; }
    public bool AllowPolicyMutation { get; set; } = true;

    private readonly string _statusPath;

    public GuardStatusHarness(string root, string statusPath, ManualTimeProvider time)
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

      var runner = new GuardianIterationRunner(
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
        readCurrentPlan: () => FailPlanProbe
          ? new PowerPlanProbeResult(
              false, null, PowerPlanFailureKind.CommandFailed, "test probe failure")
          : new PowerPlanProbeResult(
              true,
              new PowerSchemeInfo(PowerPlanCatalogProvider.HighPerformancePlanGuid, null),
              PowerPlanFailureKind.None,
              string.Empty),
        applyPlan: (target, _) =>
          Task.FromResult(new PowerPlanApplyResult(
            true, target, target, PowerPlanFailureKind.None, string.Empty)),
        timeProvider: time,
        readForeground: () => ForegroundPath is { } path
          ? ForegroundObservation.Identified(path)
          : ForegroundObservation.Unknown);
      Runner = runner;
    }

    public GuardianIterationRunner Runner { get; }

    public void Run()
    {
      Runner.RunAsync(
        _state,
        new GuardianIterationContext(
          AllowPolicyMutation,
          ResetIdleEpoch: false,
          ForcePowerRefresh: false,
          AbsorbCurrentPlanAsBaseline: false,
          PowerLifecyclePhase.Running),
        CancellationToken.None).GetAwaiter().GetResult();
    }

    public string ReadRawStatus() => File.ReadAllText(_statusPath);

    public StatusPayload ReadStatus()
      => StatusJsonReader.TryRead(_statusPath) ?? throw new InvalidOperationException("status.json 未发布");

    public void Dispose()
    {
    }
  }
}
