using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

public class FinalBrightnessObservationTests
{
  [Theory]
  [InlineData(40)]
  [InlineData(-1)]
  public async Task Final_readback_is_the_only_brightness_fact_used_by_status_heartbeat_and_notification(int observed)
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuardFinalBrightness_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var config = GuardConfig.CreateDefault(root);
      config.NotifyOnPlanChange = true;
      config.HeartbeatIntervalMin = 1;
      config.BrightnessRestoreMs = 0;
      config.BrightnessRetryCount = 1;
      config.BrightnessRetryDelayMs = 0;
      var switched = false;
      var events = new List<(string Name, string Message)>();
      var probe = new WmiProbeProcess((_, arguments, _, _) => Task.FromResult(
        arguments.Contains("brightness-write", StringComparison.Ordinal)
          ? new PowerCfgCommandResult(true, false, 0, "SGWMI1|write|ok", "")
          : observed >= 0
            ? new PowerCfgCommandResult(true, false, 0, "SGWMI1|brightness|" + observed, "")
            : new PowerCfgCommandResult(true, false, 3, "SGWMI1|error|unavailable", "")));
      var path = Path.Combine(root, "status.json");
      var runner = new GuardianIterationRunner(new IdleTracker(), new BrightnessService(probe), new StatusPublisher(path),
        () => GuardConfigReadResult.FromConfig(config),
        (_, _, message, name, _, _) => { events.Add((name, message)); return GuardianLogWriteResult.AcceptedByPipeline; },
        _ => { }, false, new HeartbeatSchedule(), readIdleSeconds: () => 0, readBatteryInfo: _ => (90, true),
        readCurrentPlan: () => new(true, new PowerSchemeInfo(switched ? config.ActivePlanGuid : config.BalancedPlanGuid, null), PowerPlanFailureKind.None, ""),
        applyPlan: (target, _) =>
        {
          switched = true;
          return Task.FromResult(new PowerPlanApplyResult(true, target, target, PowerPlanFailureKind.None, ""));
        }, readBrightness: () => 50);
      await runner.RunAsync(new GuardianLoopIterationState { PlanCatalog = new Dictionary<Guid, string>() },
        GuardianIterationContext.Running, CancellationToken.None);
      var status = StatusJsonReader.TryRead(path)!;
      status.brightness.Should().Be(observed, "a prior 50% observation cannot replace the actual final readback");
      var heartbeat = events.Single(x => x.Name == EventNameRegistry.HeartbeatTick).Message;
      if (observed >= 0) heartbeat.Should().Contain("40%").And.NotContain("50%");
      else heartbeat.Should().Contain("无法读取亮度").And.NotContain("50%");
      status.notificationEvent!.body.Should().NotContain("-1%");
      if (observed >= 0) status.notificationEvent.body.Should().Contain("40%");
      else status.notificationEvent.body.Should().Contain("尚未确认");
      events.Single(x => x.Name == EventNameRegistry.PowerPlanSwitched).Message.Should().NotContain("-1%");
    }
    finally { Directory.Delete(root, true); }
  }
}
