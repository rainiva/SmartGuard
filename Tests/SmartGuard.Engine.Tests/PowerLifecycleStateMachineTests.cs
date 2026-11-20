using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class PowerLifecycleStateMachineTests
{
  [Fact]
  public void Suspend_blocks_policy_until_resume_stabilization_finishes()
  {
    var time = new ManualTimeProvider();
    var machine = new PowerLifecycleStateMachine(time, TimeSpan.FromSeconds(5));

    machine.Observe(new(PowerLifecycleKind.Suspending, time.GetUtcNow()));
    machine.TakeDirective().AllowPolicyMutation.Should().BeFalse();

    machine.Observe(new(PowerLifecycleKind.Resumed, time.GetUtcNow()));
    var first = machine.TakeDirective();
    first.ResetIdleEpoch.Should().BeTrue();
    first.AbsorbCurrentPlanAsBaseline.Should().BeTrue();
    first.AllowPolicyMutation.Should().BeFalse();

    time.Advance(TimeSpan.FromSeconds(5));
    machine.TakeDirective().AllowPolicyMutation.Should().BeTrue();
  }

  [Fact]
  public void Duplicate_resume_does_not_repeat_reset_after_first_directive()
  {
    var time = new ManualTimeProvider();
    var machine = new PowerLifecycleStateMachine(time, TimeSpan.FromSeconds(5));
    var signal = new PowerLifecycleSignal(PowerLifecycleKind.Resumed, time.GetUtcNow());

    machine.Observe(signal);
    machine.Observe(signal);

    machine.TakeDirective().ResetIdleEpoch.Should().BeTrue();
    machine.TakeDirective().ResetIdleEpoch.Should().BeFalse();
  }

  [Fact]
  public void Suspend_and_resume_observed_before_consumption_are_preserved_in_order()
  {
    var time = new ManualTimeProvider();
    var machine = new PowerLifecycleStateMachine(time, TimeSpan.FromSeconds(5));

    machine.Observe(new(PowerLifecycleKind.Suspending, time.GetUtcNow()));
    time.Advance(TimeSpan.FromHours(1));
    machine.Observe(new(PowerLifecycleKind.Resumed, time.GetUtcNow()));

    machine.TakeDirective().TransitionsToLog.Select(x => x.Kind)
      .Should().Equal(PowerLifecycleKind.Suspending, PowerLifecycleKind.Resumed);
  }

  [Fact]
  public void Missing_resume_enters_bounded_recovery_without_enabling_policy_immediately()
  {
    var time = new ManualTimeProvider();
    var machine = new PowerLifecycleStateMachine(time, TimeSpan.FromSeconds(5));
    machine.Observe(new(PowerLifecycleKind.Suspending, time.GetUtcNow()));
    machine.TakeDirective().AllowPolicyMutation.Should().BeFalse();

    time.Advance(TimeSpan.FromSeconds(30));
    var recovery = machine.TakeDirective();

    recovery.RecoveryForced.Should().BeTrue();
    recovery.ResetIdleEpoch.Should().BeTrue();
    recovery.AbsorbCurrentPlanAsBaseline.Should().BeTrue();
    recovery.AllowPolicyMutation.Should().BeFalse();
    recovery.Phase.Should().Be(PowerLifecyclePhase.ResumeStabilizing);
  }

  [Fact]
  public void Late_resume_after_forced_recovery_is_logged_without_repeating_reset()
  {
    var time = new ManualTimeProvider();
    var machine = new PowerLifecycleStateMachine(time, TimeSpan.FromSeconds(5));
    machine.Observe(new(PowerLifecycleKind.Suspending, time.GetUtcNow()));
    machine.TakeDirective();
    time.Advance(TimeSpan.FromSeconds(30));
    machine.TakeDirective().ResetIdleEpoch.Should().BeTrue();

    machine.Observe(new(PowerLifecycleKind.Resumed, time.GetUtcNow()));
    var lateResume = machine.TakeDirective();

    lateResume.TransitionsToLog.Should().ContainSingle(x =>
      x.Kind == PowerLifecycleKind.Resumed);
    lateResume.ResetIdleEpoch.Should().BeFalse();
    lateResume.AbsorbCurrentPlanAsBaseline.Should().BeFalse();
    lateResume.AllowPolicyMutation.Should().BeFalse();
  }

  [Fact]
  public void Transition_queue_overflow_is_explicit_and_keeps_the_latest_signal()
  {
    var time = new ManualTimeProvider();
    var machine = new PowerLifecycleStateMachine(time, TimeSpan.FromSeconds(5));

    for (var i = 0; i < 5; i++)
    {
      machine.Observe(new(PowerLifecycleKind.Suspending, time.GetUtcNow()));
      machine.Observe(new(PowerLifecycleKind.Resumed, time.GetUtcNow()));
    }

    var directive = machine.TakeDirective();

    directive.TransitionOverflowed.Should().BeTrue();
    directive.TransitionsToLog.Should().HaveCount(8);
    directive.TransitionsToLog.First().Kind.Should().Be(PowerLifecycleKind.Suspending);
    directive.TransitionsToLog.Last().Kind.Should().Be(PowerLifecycleKind.Resumed);
  }
}
