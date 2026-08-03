using SmartGuard.Configuration;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class LogConsumerRecoveryTests
{
  private static LogEvent Event(string message)
    => LogEventFactory.Create(
      LogSeverity.Info,
      LogComponentRegistry.Engine,
      LogChannelRegistry.Lifecycle,
      EventNameRegistry.AppStarted,
      message);

  private static LogEvent[] Batch(string message) => new[] { Event(message) };

  [Fact]
  public void ExecuteWithRecovery_returns_true_when_batch_succeeds()
  {
    var sm = new LogRecoveryStateMachine(failureThreshold: 3);
    var recovery = new LogConsumerRecovery(sm);

    var result = recovery.ExecuteWithRecovery(_ => true, Batch("line1"), "fallback.log");
    result.Should().BeTrue();
    sm.State.Should().Be(RecoveryState.Healthy);
  }

  [Fact]
  public void ExecuteWithRecovery_records_failure_when_batch_fails()
  {
    var sm = new LogRecoveryStateMachine(failureThreshold: 3);
    var recovery = new LogConsumerRecovery(sm);

    recovery.ExecuteWithRecovery(_ => false, Batch("line1"), "fallback.log");
    sm.State.Should().Be(RecoveryState.Degraded);
  }

  [Fact]
  public void ExecuteWithRecovery_enters_Failed_after_threshold_failures()
  {
    var sm = new LogRecoveryStateMachine(failureThreshold: 2);
    var recovery = new LogConsumerRecovery(sm);
    var batch = Batch("line1");

    recovery.ExecuteWithRecovery(_ => false, batch, "fallback.log");
    recovery.ExecuteWithRecovery(_ => false, batch, "fallback.log");
    sm.State.Should().Be(RecoveryState.Failed);
  }

  [Fact]
  public void ExecuteWithRecovery_isolates_exceptions_from_write_callback()
  {
    var sm = new LogRecoveryStateMachine(failureThreshold: 3);
    var recovery = new LogConsumerRecovery(sm);

    var result = recovery.ExecuteWithRecovery(
      _ => throw new InvalidOperationException("disk error"),
      Batch("line1"),
      "fallback.log");

    result.Should().BeTrue();
    sm.State.Should().Be(RecoveryState.Degraded);
  }
}
