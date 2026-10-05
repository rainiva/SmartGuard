using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Worker;
using SmartGuard.Contracts;

namespace SmartGuard.Engine.Tests;

public class ForegroundObservationWindowTests
{
  [Fact]
  public async Task Short_windows_do_not_bypass_the_full_evaluation_floor_or_spin_at_expiry()
  {
    var config = Config();
    config.ForegroundObservationSec = 1;
    var state = new GuardianLoopIterationState();
    var time = new ManualTimeProvider();
    var path = config.ForegroundProgramPaths[0];
    var full = 0;
    using var wake = new SemaphoreSlim(0);
    var coordinator = new GuardianCycleCoordinator(() => new(config, true, RuleFingerprint: "rules"),
      () => ForegroundObservation.Identified(path), state, wake, (_, _) =>
      {
        full++;
        Observe(state, config, path, time);
        state.LastForegroundAdmitted = path == config.ForegroundProgramPaths[0];
        return Task.CompletedTask;
      }, timeProvider: time);
    var directive = new SmartGuard.Engine.Infrastructure.PowerLifecycleStateMachine(time).TakeDirective();
    await coordinator.RunCycleAsync(directive, 60, CancellationToken.None);
    path = @"C:\Other\app.exe";
    time.Advance(TimeSpan.FromSeconds(1));
    coordinator.MarkForegroundSignal();
    await coordinator.RunCycleAsync(directive, 60, CancellationToken.None);
    time.Advance(TimeSpan.FromSeconds(1));
    await coordinator.RunCycleAsync(directive, 60, CancellationToken.None);
    full.Should().Be(1);
    coordinator.ComputeWaitDelay().Should().Be(TimeSpan.FromSeconds(3));
    time.Advance(TimeSpan.FromSeconds(3));
    await coordinator.RunCycleAsync(directive, 60, CancellationToken.None);
    full.Should().Be(2);
    state.ForegroundObservationPhase.Should().Be("expired");
  }

  private static GuardConfig Config() => new()
  {
    ForegroundRestrictionEnabled = true,
    ForegroundProgramPaths = [@"C:\Apps\wps.exe", @"C:\Apps\et.exe"],
    ForegroundObservationSec = 180,
  };

  private static void Observe(GuardianLoopIterationState state, GuardConfig config, string? path,
    ManualTimeProvider time, string fingerprint = "rules")
  {
    var foreground = path is null ? ForegroundObservation.Unknown : ForegroundObservation.Identified(path);
    state.UpdateForegroundObservation(new(config, true, RuleFingerprint: fingerprint),
      PolicyEngine.EvaluateAdmission(foreground, config), foreground, time);
  }

  [Fact]
  public void First_nonlisted_unknown_and_list_to_list_transitions_do_not_start_a_window()
  {
    var state = new GuardianLoopIterationState();
    var config = Config();
    var time = new ManualTimeProvider();
    Observe(state, config, @"C:\Other\app.exe", time);
    Observe(state, config, null, time);
    Observe(state, config, config.ForegroundProgramPaths[0], time);
    Observe(state, config, config.ForegroundProgramPaths[1], time);
    state.ForegroundObservationPhase.Should().BeNull();
    state.IsForegroundObserving.Should().BeFalse();
  }

  [Fact]
  public void Return_inside_window_is_immediate_and_a_new_exit_gets_a_full_window()
  {
    var state = new GuardianLoopIterationState();
    var config = Config();
    var time = new ManualTimeProvider();
    Observe(state, config, config.ForegroundProgramPaths[0], time);
    Observe(state, config, @"C:\Other\app.exe", time);
    time.Advance(TimeSpan.FromSeconds(170));
    Observe(state, config, config.ForegroundProgramPaths[1], time);
    state.ForegroundObservationPhase.Should().Be("returned");
    state.IsForegroundObserving.Should().BeFalse();
    Observe(state, config, config.ForegroundProgramPaths[0], time);
    state.ForegroundObservationPhase.Should().BeNull("listed-to-listed is not another return from outside");
    Observe(state, config, @"C:\Other\app.exe", time);
    state.ForegroundObservationRemaining(time).Should().Be(TimeSpan.FromSeconds(180));
  }

  [Fact]
  public void Wall_clock_changes_cannot_extend_or_expire_the_window()
  {
    var state = new GuardianLoopIterationState();
    var config = Config();
    var time = new ManualTimeProvider();
    Observe(state, config, config.ForegroundProgramPaths[0], time);
    Observe(state, config, @"C:\Other\app.exe", time);
    time.SetUtcNow(time.GetUtcNow().AddYears(1));
    Observe(state, config, @"C:\Other\app.exe", time);
    state.IsForegroundObserving.Should().BeTrue();
    time.AdvanceTimestamp(TimeSpan.FromSeconds(180));
    Observe(state, config, @"C:\Other\app.exe", time);
    state.ForegroundObservationPhase.Should().Be("expired");
  }

  [Theory]
  [InlineData("paused")]
  [InlineData("manual")]
  [InlineData("disabled")]
  [InlineData("rules")]
  [InlineData("resume")]
  public void Authority_changes_discard_old_windows_and_do_not_manufacture_a_new_exit(string change)
  {
    var state = new GuardianLoopIterationState();
    var config = Config();
    var time = new ManualTimeProvider();
    Observe(state, config, config.ForegroundProgramPaths[0], time);
    Observe(state, config, @"C:\Other\app.exe", time);
    config.Paused = change == "paused";
    config.ForegroundRestrictionEnabled = change != "disabled";
    if (change == "manual") config.ManualHighPerformanceUntil = time.GetLocalNow().LocalDateTime.AddMinutes(1);
    if (change == "resume") state.ResetForegroundObservation();
    Observe(state, config, @"C:\Other\app.exe", time, change == "rules" ? "new-rules" : "rules");
    state.ForegroundObservationPhase.Should().BeNull();
    state.IsForegroundObserving.Should().BeFalse();
  }

  [Fact]
  public void Unknown_identity_does_not_claim_a_confirmed_background_transition()
  {
    var tracker = new GuardObservationTracker();
    tracker.ObserveAdmission(new(false, GuardDecisionReason.ForegroundIdentityUnknown), null,
      DateTimeOffset.UtcNow, "observing", 180, "wps.exe");
    var messages = new List<string>();
    tracker.Drain(Config(), (_, _, message, _, _, _) =>
    { messages.Add(message); return GuardianLogWriteResult.AcceptedByPipeline; });
    messages.Single().Should().Contain("无法识别").And.NotContain("已切换至后台");
  }
}
