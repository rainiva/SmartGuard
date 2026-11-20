using SmartGuard.Configuration;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class PowerLifecycleScenarioTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task Resume_sequence_is_deterministic_on_battery_and_ac(bool onAc)
  {
    using var scenario = PowerLifecycleScenario.Start(onAc, idleSeconds: 290);

    await scenario.SuspendAsync();
    scenario.OsChangesPlanTo(PowerPlanCatalogProvider.BalancedPlanGuid);
    scenario.Advance(TimeSpan.FromHours(1));
    await scenario.ResumeByMouseAsync(apiIdleAtFirstSample: 8);

    scenario.PlanWritesDuringSuspend.Should().Be(0);
    scenario.ExternalWarningsDuringSuspend.Should().Be(0);
    scenario.FirstResumeIdle.Should().Be(0);
    scenario.ResumeHeartbeats.Should().Be(1);
    scenario.StatusAge.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(60));
  }

  [Fact]
  public async Task Plan_probe_failure_does_not_create_false_success_or_notification()
  {
    using var scenario = PowerLifecycleScenario.Start(onAc: true, idleSeconds: 0);
    scenario.FailNextPlanProbe(PowerPlanFailureKind.TimedOut);

    await scenario.RunIterationAsync();

    scenario.PlanWrites.Should().Be(0);
    scenario.SuccessfulPlanEvents.Should().Be(0);
    scenario.PlanNotifications.Should().Be(0);
    scenario.FailureEvents.Should().ContainSingle();
  }
}

internal sealed class PowerLifecycleScenario : IDisposable
{
  private readonly GuardianLifecycleHarness _harness;
  private DateTimeOffset? _suspendAt;
  private DateTimeOffset? _resumeAt;
  private int _heartbeatCountBeforeResume;

  private PowerLifecycleScenario(bool onAc, int idleSeconds)
  {
    _harness = GuardianLifecycleHarness.Create(
      PowerPlanCatalogProvider.HighPerformancePlanGuid,
      (uint)idleSeconds,
      onAc);
    _harness.EnableHeartbeat();
  }

  public static PowerLifecycleScenario Start(bool onAc, int idleSeconds)
    => new(onAc, idleSeconds);

  public async Task SuspendAsync()
  {
    _suspendAt = _harness.CurrentTime;
    _harness.Signal(PowerLifecycleKind.Suspending);
    await _harness.RunSignaledIterationAsync();
  }

  public void OsChangesPlanTo(Guid plan) => _harness.SetOsPlan(plan);

  public void Advance(TimeSpan value) => _harness.Advance(value);

  public async Task ResumeByMouseAsync(uint apiIdleAtFirstSample)
  {
    _heartbeatCountBeforeResume = _harness.HeartbeatEvents;
    _resumeAt = _harness.CurrentTime;
    _harness.SetApiIdle(apiIdleAtFirstSample);
    _harness.Signal(PowerLifecycleKind.Resumed);
    await _harness.RunSignaledIterationAsync();
  }

  public int PlanWritesDuringSuspend
    => CountDuringSuspend(EventNameRegistry.PowerPlanSwitched);

  public int ExternalWarningsDuringSuspend
    => CountDuringSuspend(EventNameRegistry.ExternalPlanChange);

  public int FirstResumeIdle => _harness.LastPublishedIdle;

  public int ResumeHeartbeats => _harness.HeartbeatEvents - _heartbeatCountBeforeResume;

  public TimeSpan StatusAge
    => _harness.LastStatusTimestamp is { } timestamp
      ? _harness.CurrentTime - timestamp
      : TimeSpan.MaxValue;

  public int PlanWrites => _harness.PlanWrites;
  public int SuccessfulPlanEvents => _harness.SuccessfulPlanEvents;
  public int PlanNotifications => _harness.PlanNotifications;
  public IReadOnlyList<HarnessEvent> FailureEvents
    => _harness.AllEvents
      .Where(x => x.EventName is EventNameRegistry.PowerPlanProbeFailed
        or EventNameRegistry.PowerPlanApplyFailed)
      .ToArray();

  public void FailNextPlanProbe(PowerPlanFailureKind kind)
    => _harness.FailPlanProbe(kind);

  public Task RunIterationAsync() => _harness.RunNormalIterationAsync();

  private int CountDuringSuspend(string eventName)
    => _harness.AllEvents.Count(x => x.EventName == eventName
      && _suspendAt is { } suspendAt
      && x.ObservedAtUtc >= suspendAt
      && (_resumeAt is null || x.ObservedAtUtc < _resumeAt.Value));

  public void Dispose() => _harness.Dispose();
}
