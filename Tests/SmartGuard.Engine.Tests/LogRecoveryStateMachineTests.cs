using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class LogRecoveryStateMachineTests
{
  [Fact]
  public void Recovery_uses_elapsed_monotonic_time_even_when_wall_clock_moves_backwards()
  {
    var clock = new RecoveryClock();
    var constructor = typeof(LogRecoveryStateMachine).GetConstructor([typeof(int), typeof(int), typeof(TimeProvider)]);
    constructor.Should().NotBeNull("cooldown must be testable independently of system UTC changes");
    var state = (LogRecoveryStateMachine)constructor!.Invoke([1, 30, clock]);
    state.RecordFailure();
    clock.Elapsed = TimeSpan.FromSeconds(29);
    state.TryRecover().Should().BeFalse();
    clock.Elapsed = TimeSpan.FromSeconds(30);
    state.TryRecover().Should().BeTrue();
  }

  private sealed class RecoveryClock : TimeProvider
  {
    public TimeSpan Elapsed;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Elapsed.Ticks;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch - Elapsed;
  }

  [Fact]
  public void Initial_state_is_Healthy()
  {
    var sm = new LogRecoveryStateMachine(failureThreshold: 3);
    sm.State.Should().Be(RecoveryState.Healthy);
  }

  [Fact]
  public void Transitions_to_Degraded_after_single_failure()
  {
    var sm = new LogRecoveryStateMachine(failureThreshold: 3);
    sm.RecordFailure();
    sm.State.Should().Be(RecoveryState.Degraded);
  }

  [Fact]
  public void Transitions_to_Failed_after_threshold_consecutive_failures()
  {
    var sm = new LogRecoveryStateMachine(failureThreshold: 3);
    sm.RecordFailure();
    sm.RecordFailure();
    sm.RecordFailure();
    sm.State.Should().Be(RecoveryState.Failed);
  }

  [Fact]
  public void Success_resets_failure_count_in_Degraded_state()
  {
    var sm = new LogRecoveryStateMachine(failureThreshold: 3);
    sm.RecordFailure();
    sm.RecordFailure();
    sm.RecordSuccess();
    sm.State.Should().Be(RecoveryState.Healthy);
  }

  [Fact]
  public void TryRecover_from_Failed_returns_true_after_recovery_interval()
  {
    var sm = new LogRecoveryStateMachine(failureThreshold: 2, recoveryIntervalSeconds: 0);
    sm.RecordFailure();
    sm.RecordFailure();
    sm.State.Should().Be(RecoveryState.Failed);

    sm.TryRecover().Should().BeTrue();
    sm.State.Should().Be(RecoveryState.Degraded);
  }

  [Fact]
  public void TryRecover_from_Failed_returns_false_before_recovery_interval()
  {
    var sm = new LogRecoveryStateMachine(failureThreshold: 2, recoveryIntervalSeconds: 3600);
    sm.RecordFailure();
    sm.RecordFailure();

    sm.TryRecover().Should().BeFalse();
    sm.State.Should().Be(RecoveryState.Failed);
  }

  [Fact]
  public void Success_from_Failed_after_recover_resets_to_Healthy()
  {
    var sm = new LogRecoveryStateMachine(failureThreshold: 2, recoveryIntervalSeconds: 0);
    sm.RecordFailure();
    sm.RecordFailure();
    sm.TryRecover();
    sm.RecordSuccess();
    sm.State.Should().Be(RecoveryState.Healthy);
  }
}
