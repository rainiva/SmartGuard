using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

public class BrightnessPolicyReloadTests
{
  [Fact]
  public async Task Real_brightness_query_allows_next_cycle_to_adopt_conditions_and_foreground()
  {
    var root = Path.Combine(Path.GetTempPath(), "BrightnessPolicy_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var repository = new GuardConfigRepository(SmartGuardPaths.ConfigFile(root));
      var config = GuardConfig.CreateDefault(root);
      config.ForegroundRestrictionEnabled = true;
      config.ForegroundProgramPaths = [@"C:\Apps\Editor.exe"];
      config.BalancedThresholdSec = 300;
      repository.Save(config);
      var foreground = ForegroundObservation.Identified(@"C:\Apps\Other.exe");
      var current = config.BalancedPlanGuid;
      var statusPath = SmartGuardPaths.StatusFile(root);
      var state = new GuardianLoopIterationState
      {
        PlanCatalog = new Dictionary<Guid, string>
        {
          [config.ActivePlanGuid] = "active",
          [config.BalancedPlanGuid] = "balanced",
          [config.PowerSaverPlanGuid] = "saver",
        },
      };
      var runner = new GuardianIterationRunner(
        new IdleTracker(), new BrightnessService(), new StatusPublisher(statusPath),
        () => GuardConfigReadResult.TryFromRepository(repository),
        (_, _, _, _, _, _) => GuardianLogWriteResult.AcceptedByPipeline,
        _ => { }, () => false, new HeartbeatSchedule(),
        readIdleSeconds: () => 600,
        readBatteryInfo: _ => (100, true),
        readCurrentPlan: () => new(true, new PowerSchemeInfo(current, null), PowerPlanFailureKind.None, ""),
        // No test may change the physical power plan or brightness. These facts
        // already match the intended target; the real WMI read stays in place.
        applyPlan: (_, _) => throw new InvalidOperationException("Unexpected physical action"),
        syncPlanBrightness: (_, _) => throw new InvalidOperationException("Unexpected brightness action"),
        readForeground: () => foreground);
      var context = new GuardianIterationContext(true, false, false, false, PowerLifecyclePhase.Running);
      await runner.RunAsync(state, context, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
      StatusJsonReader.TryRead(statusPath)!.guardReason.Should().Be(GuardStatusCodes.Reasons.WaitingForListedProgram);

      config.BalancedThresholdSec = 900;
      repository.Save(config);
      current = config.ActivePlanGuid;
      foreground = ForegroundObservation.Identified(@"C:\Apps\Editor.exe");
      await runner.RunAsync(state, context, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
      var status = StatusJsonReader.TryRead(statusPath)!;
      status.guardReason.Should().Be(GuardStatusCodes.Reasons.ForegroundMatched);
      status.expectedPlan.Should().Be("active", "the new 900-second condition keeps 600 seconds of idle in the active tier");
      status.brightness.Should().BeInRange(-1, 100);
    }
    finally { Directory.Delete(root, true); }
  }
}
