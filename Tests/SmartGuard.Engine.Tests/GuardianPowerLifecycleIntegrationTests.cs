using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

public class GuardianPowerLifecycleIntegrationTests
{
  [Fact]
  public void Suspend_context_disallows_policy_mutation_and_preserves_lifecycle_phase()
  {
    var context = new GuardianIterationContext(
      AllowPolicyMutation: false,
      ResetIdleEpoch: true,
      ForcePowerRefresh: true,
      AbsorbCurrentPlanAsBaseline: true,
      LifecyclePhase: PowerLifecyclePhase.ResumeStabilizing);

    context.AllowPolicyMutation.Should().BeFalse();
    context.ResetIdleEpoch.Should().BeTrue();
    context.AbsorbCurrentPlanAsBaseline.Should().BeTrue();
    context.LifecyclePhase.Should().Be(PowerLifecyclePhase.ResumeStabilizing);
  }

  [Fact]
  public async Task Suspend_os_balance_mouse_resume_does_not_fight_os_or_preserve_sleep_idle()
  {
    using var harness = GuardianLifecycleHarness.Create(
      PowerPlanCatalogProvider.HighPerformancePlanGuid,
      apiIdle: 290);

    harness.Signal(PowerLifecycleKind.Suspending);
    harness.SetOsPlan(PowerPlanCatalogProvider.BalancedPlanGuid);
    await harness.RunSignaledIterationAsync();

    harness.AppliedPlans.Should().BeEmpty();
    harness.ExternalChangeEvents.Should().BeEmpty();

    harness.Advance(TimeSpan.FromMinutes(68));
    harness.Signal(PowerLifecycleKind.Resumed);
    harness.SetApiIdle(8);
    await harness.RunSignaledIterationAsync();

    harness.LastPublishedIdle.Should().Be(0);
    harness.AppliedPlans.Should().BeEmpty();
    harness.LastKnownPlan.Should().Be(PowerPlanCatalogProvider.BalancedPlanGuid);

    harness.Advance(TimeSpan.FromSeconds(5));
    await harness.RunSignaledIterationAsync();

    harness.AppliedPlans.Should().Equal(PowerPlanCatalogProvider.HighPerformancePlanGuid);
  }

  [Fact]
  public async Task External_plan_change_while_running_is_reported_and_corrected_once()
  {
    using var harness = GuardianLifecycleHarness.Create(
      PowerPlanCatalogProvider.HighPerformancePlanGuid,
      apiIdle: 10);

    harness.SetOsPlan(PowerPlanCatalogProvider.BalancedPlanGuid);
    await harness.RunNormalIterationAsync();

    harness.ExternalChangeEvents.Should().ContainSingle();
    harness.AppliedPlans.Should().Equal(PowerPlanCatalogProvider.HighPerformancePlanGuid);
  }

  [Fact]
  public async Task Failed_plan_probe_does_not_correct_or_report_external_change()
  {
    using var harness = GuardianLifecycleHarness.Create(
      PowerPlanCatalogProvider.HighPerformancePlanGuid,
      apiIdle: 10);

    harness.SetOsPlan(PowerPlanCatalogProvider.BalancedPlanGuid);
    harness.FailPlanProbe("Access is denied.");
    await harness.RunNormalIterationAsync();

    harness.AppliedPlans.Should().BeEmpty();
    harness.ExternalChangeEvents.Should().BeEmpty();
    harness.Events.Should().ContainSingle(x => x.EventName == EventNameRegistry.PowerPlanProbeFailed);
  }

  [Fact]
  public async Task Failed_plan_apply_does_not_report_success_or_mark_script_switch()
  {
    using var harness = GuardianLifecycleHarness.Create(
      PowerPlanCatalogProvider.HighPerformancePlanGuid,
      apiIdle: 10);

    harness.SetOsPlan(PowerPlanCatalogProvider.BalancedPlanGuid);
    harness.FailNextPlanApply();
    await harness.RunNormalIterationAsync();

    harness.AppliedPlans.Should().BeEmpty();
    harness.Events.Should().ContainSingle(x => x.EventName == EventNameRegistry.PowerPlanApplyFailed);
    harness.Events.Should().NotContain(x => x.EventName == EventNameRegistry.PowerPlanSwitched);
  }

  [Fact]
  public async Task Suspend_and_resume_queued_while_loop_is_paused_are_logged_in_observed_order()
  {
    using var harness = GuardianLifecycleHarness.Create(
      PowerPlanCatalogProvider.HighPerformancePlanGuid,
      apiIdle: 0);

    harness.Signal(PowerLifecycleKind.Suspending);
    harness.Advance(TimeSpan.FromHours(1));
    harness.Signal(PowerLifecycleKind.Resumed);

    await harness.RunSignaledIterationAsync();

    harness.LifecycleEvents.Select(x => x.EventName).Should().Equal(
      EventNameRegistry.PowerSuspendObserved,
      EventNameRegistry.PowerResumeObserved);
    harness.LifecycleEvents.Select(x => x.ObservedAtUtc).Should().BeInAscendingOrder();
  }

  [Fact]
  public async Task Missing_resume_recovers_baseline_but_cannot_mutate_plan_until_stable()
  {
    using var harness = GuardianLifecycleHarness.Create(
      PowerPlanCatalogProvider.HighPerformancePlanGuid,
      apiIdle: 290);

    harness.Signal(PowerLifecycleKind.Suspending);
    await harness.RunSignaledIterationAsync();

    harness.Advance(TimeSpan.FromSeconds(30));
    await harness.RunNormalIterationAsync();

    harness.Events.Should().ContainSingle(x =>
      x.EventName == EventNameRegistry.PowerLifecycleRecoveryForced);
    harness.LastPublishedIdle.Should().Be(0);
    harness.AppliedPlans.Should().BeEmpty();
  }

  [Fact]
  public void Lifecycle_event_factory_preserves_original_observation_time()
  {
    var observedAt = new DateTimeOffset(2026, 8, 22, 14, 0, 0, TimeSpan.Zero);
    var signal = new PowerLifecycleSignal(PowerLifecycleKind.Resumed, observedAt);

    var logEvent = EngineLogEvents.CreateLifecycle(
      LogLevel.Info,
      EventNameRegistry.PowerResumeObserved,
      "resume",
      signal);

    logEvent.Properties["lifecycleObservedAtUtc"].GetDateTimeOffset()
      .Should().Be(observedAt);
  }
}

internal sealed class GuardianLifecycleHarness : IDisposable
{
  private readonly ManualTimeProvider _time = new();
  private readonly PowerLifecycleStateMachine _lifecycle;
  private readonly GuardianLoopIterationState _state = new();
  private readonly GuardConfig _config;
  private readonly StatusPublisher _publisher;
  private readonly string _root;
  private readonly GuardianIterationRunner _runner;
  private Guid _activePlan;
  private uint _apiIdle;
  private PowerPlanProbeResult? _forcedProbeFailure;
  private bool _failNextApply;

  private GuardianLifecycleHarness(Guid activePlan, uint apiIdle)
  {
    _root = Path.Combine(Path.GetTempPath(), "SmartGuardLifecycleHarness_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_root);
    _activePlan = activePlan;
    _apiIdle = apiIdle;
    _config = GuardConfig.CreateDefault(_root);
    _config.BrightnessRestoreMs = 0;
    _config.HeartbeatIntervalMin = 0;
    _publisher = new StatusPublisher(Path.Combine(_root, "SmartGuard.status.json"));
    _lifecycle = new PowerLifecycleStateMachine(_time);
    _state.PlanCatalog = new Dictionary<Guid, string>
    {
      [PowerPlanCatalogProvider.HighPerformancePlanGuid] = PowerPlanCatalogProvider.HighPerformanceDisplayName,
      [PowerPlanCatalogProvider.BalancedPlanGuid] = PowerPlanCatalogProvider.BalancedDisplayName,
      [PowerPlanCatalogProvider.PowerSaverPlanGuid] = PowerPlanCatalogProvider.PowerSaverDisplayName,
    };
    _state.LastKnownGuid = activePlan;
    _runner = new GuardianIterationRunner(
      new IdleTracker(_time),
      new BrightnessService(),
      _publisher,
      () => _config,
      (config, level, message, eventName) => Events.Add(new(eventName, _time.GetUtcNow(), message)),
      _ => { },
      powerCfgBrightnessSupported: false,
      readIdleSeconds: () => _apiIdle,
      readBatteryInfo: _ => (70, true),
      readCurrentPlan: () => _forcedProbeFailure ?? new PowerPlanProbeResult(
        true,
        new PowerSchemeInfo(_activePlan, null),
        PowerPlanFailureKind.None,
        string.Empty),
      applyPlan: (target, cancellationToken) =>
      {
        if (_failNextApply)
        {
          _failNextApply = false;
          return Task.FromResult(new PowerPlanApplyResult(
            false,
            target,
            _activePlan,
            PowerPlanFailureKind.VerificationMismatch,
            "test verification mismatch"));
        }

        _activePlan = target;
        AppliedPlans.Add(target);
        return Task.FromResult(new PowerPlanApplyResult(
          true,
          target,
          target,
          PowerPlanFailureKind.None,
          string.Empty));
      });
  }

  public List<Guid> AppliedPlans { get; } = [];
  public List<HarnessEvent> Events { get; } = [];
  public IReadOnlyList<HarnessEvent> LifecycleEvents
    => Events.Where(x => x.EventName is EventNameRegistry.PowerSuspendObserved or EventNameRegistry.PowerResumeObserved).ToArray();
  public IReadOnlyList<HarnessEvent> ExternalChangeEvents
    => Events.Where(x => x.EventName == EventNameRegistry.ExternalPlanChange).ToArray();
  public Guid LastKnownPlan => _activePlan;
  public int LastPublishedIdle { get; private set; }

  public static GuardianLifecycleHarness Create(Guid activePlan, uint apiIdle)
    => new(activePlan, apiIdle);

  public void Signal(PowerLifecycleKind kind)
    => _lifecycle.Observe(new(kind, _time.GetUtcNow()));

  public void SetOsPlan(Guid plan) => _activePlan = plan;

  public void FailPlanProbe(string diagnostic)
    => _forcedProbeFailure = new PowerPlanProbeResult(
      false,
      null,
      PowerPlanFailureKind.CommandFailed,
      diagnostic);

  public void FailNextPlanApply() => _failNextApply = true;

  public void SetApiIdle(uint idle) => _apiIdle = idle;

  public void Advance(TimeSpan value) => _time.Advance(value);

  public Task RunSignaledIterationAsync() => RunIterationAsync();

  public Task RunNormalIterationAsync() => RunIterationAsync();

  private async Task RunIterationAsync()
  {
    var directive = _lifecycle.TakeDirective();
    foreach (var signal in directive.TransitionsToLog)
    {
      var eventName = signal.Kind switch
      {
        PowerLifecycleKind.Suspending => EventNameRegistry.PowerSuspendObserved,
        PowerLifecycleKind.Resumed => EventNameRegistry.PowerResumeObserved,
        _ => EventNameRegistry.PowerSourceChanged,
      };
      Events.Add(new(eventName, signal.ObservedAtUtc, signal.Kind.ToString()));
    }

    if (directive.RecoveryForced)
      Events.Add(new(EventNameRegistry.PowerLifecycleRecoveryForced, _time.GetUtcNow(), "recovery"));

    if (!directive.RunIteration)
      return;

    await _runner.RunAsync(
      _state,
      new GuardianIterationContext(
        directive.AllowPolicyMutation,
        directive.ResetIdleEpoch,
        directive.ForcePowerRefresh,
        directive.AbsorbCurrentPlanAsBaseline,
        directive.Phase),
      CancellationToken.None);

    var statusPath = Path.Combine(_root, "SmartGuard.status.json");
    if (File.Exists(statusPath))
    {
      using var document = JsonDocument.Parse(File.ReadAllText(statusPath));
      LastPublishedIdle = document.RootElement.GetProperty("idleSeconds").GetInt32();
    }
  }

  public void Dispose()
  {
    try { Directory.Delete(_root, recursive: true); } catch { }
  }
}

internal sealed record HarnessEvent(string EventName, DateTimeOffset ObservedAtUtc, string Message);
