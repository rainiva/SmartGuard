using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

public class PlanPublicationConsistencyTests
{
  [Theory]
  [InlineData(0)] // 最终匹配
  [InlineData(1)] // 外部改回
  [InlineData(2)] // 最终探测失败
  [InlineData(3)] // apply验证失败仍返回观测
  public async Task Final_observation_after_apply_controls_published_plan_and_success(int scenario)
  {
    var root = Path.Combine(Path.GetTempPath(), "PlanPublication_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var config = GuardConfig.CreateDefault(root);
      config.BrightnessRestoreMs = 0;
      config.HeartbeatIntervalMin = 10;
      var high = PowerPlanCatalogProvider.HighPerformancePlanGuid;
      var balanced = PowerPlanCatalogProvider.BalancedPlanGuid;
      var saver = PowerPlanCatalogProvider.PowerSaverPlanGuid;
      var time = new ManualTimeProvider();
      var path = Path.Combine(root, "SmartGuard.status.json");
      var events = new List<string>();
      var applied = false;
      var reads = 0;
      var state = new GuardianLoopIterationState
      {
        PlanCatalog = new Dictionary<Guid, string> { [high] = "high", [balanced] = "balanced", [saver] = "saver" },
        LastKnownGuid = balanced,
      };
      var runner = new GuardianIterationRunner(
        new IdleTracker(time), new BrightnessService(), new StatusPublisher(path, time),
        () => GuardConfigReadResult.FromConfig(config),
        (_, _, _, eventName, _, _) => { events.Add(eventName); return GuardianLogWriteResult.AcceptedByPipeline; },
        _ => { }, false, new HeartbeatSchedule(time),
        readIdleSeconds: () => 0,
        readBatteryInfo: _ => (100, true),
        readCurrentPlan: () =>
        {
          reads++;
          return applied && scenario == 2
            ? new(false, null, PowerPlanFailureKind.ApiFailed, "final probe unavailable")
            : new(true, new PowerSchemeInfo(applied ? scenario == 0 ? high : saver : balanced, null), PowerPlanFailureKind.None, "");
        },
        applyPlan: (target, _) =>
        {
          applied = true;
          return Task.FromResult(scenario == 3
            ? new PowerPlanApplyResult(false, target, saver, PowerPlanFailureKind.VerificationMismatch, "initial mismatch")
            : new PowerPlanApplyResult(true, target, target, PowerPlanFailureKind.None, ""));
        },
        timeProvider: time, readBrightness: () => -1);
      await runner.RunAsync(state, new(true, false, false, false, PowerLifecyclePhase.Running), CancellationToken.None);

      var status = StatusJsonReader.TryRead(path)!;
      status.currentPlanGUID.Should().Be(scenario == 2 ? null : scenario == 0 ? high.ToString() : saver.ToString());
      if (scenario == 0)
      {
        events.Should().ContainSingle(x => x == EventNameRegistry.PowerPlanSwitched);
        events.Should().NotContain(EventNameRegistry.PowerPlanApplyFailed);
        state.ScriptJustSwitched.Should().BeTrue();
      }
      else
      {
        events.Should().NotContain(EventNameRegistry.PowerPlanSwitched);
        events.Should().Contain(EventNameRegistry.PowerPlanApplyFailed);
        events.Should().NotContain(EventNameRegistry.HeartbeatTick,
          "失败轮次不能追加计划正常心跳，失败事件本身保持健康可观察");
        status.notificationEvent?.kind.Should().NotBe(NotificationKinds.PlanSwitch);
        state.ScriptJustSwitched.Should().BeFalse();
      }
      reads.Should().Be(scenario == 3 ? 1 : 2);
      if (scenario != 0)
      {
        // 后续正常轮次可以恢复，但失败轮次不能冒充已成功纠偏。
        scenario = 0;
        applied = false;
        events.Clear();
        await runner.RunAsync(state, new(true, false, false, false, PowerLifecyclePhase.Running), CancellationToken.None);
        StatusJsonReader.TryRead(path)!.currentPlanGUID.Should().Be(high.ToString());
        events.Should().ContainSingle(x => x == EventNameRegistry.PowerPlanSwitched);
      }
    }
    finally { Directory.Delete(root, true); }
  }
}
