using SmartGuard.Configuration;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

[Collection("LoggingFallbackTests")]
public class LogConsumerRecoveryTests
{
  [Fact]
  public void All_failed_sinks_must_not_be_counted_as_written()
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var oldEmergency = LogFallbackChain.EmergencyLogFileOverrideForTests;
    try
    {
      LogFallbackChain.EmergencyLogFileOverrideForTests = root;
      var recovery = new LogConsumerRecovery(new LogRecoveryStateMachine());
      var pipeline = new LogPipeline(batch => recovery.ExecuteWithRecovery(_ => false, batch, root));
      pipeline.TryEnqueue(LogPriority.High, Batch("all-failed")[0]).Should().BeTrue();
      pipeline.FlushOnce().Should().BeFalse();
      pipeline.Diagnostics.WrittenLines.Should().Be(0);
      pipeline.Diagnostics.FailedBatches.Should().Be(1);
    }
    finally
    {
      LogFallbackChain.EmergencyLogFileOverrideForTests = oldEmergency;
      Directory.Delete(root, true);
    }
  }

  [Fact]
  public void Fallback_preserves_original_event_identity_channel_and_properties()
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var original = Event("fallback") with { Properties = new Dictionary<string, System.Text.Json.JsonElement>
      { ["reason"] = System.Text.Json.JsonSerializer.SerializeToElement("test") } };
      var path = Path.Combine(root, "startup.log");
      new LogConsumerRecovery(new LogRecoveryStateMachine()).ExecuteWithRecovery(_ => false, [original], path);
      LogJsonlFormatter.TryParseLine(File.ReadAllLines(path).Single(), out var decoded).Should().BeTrue();
      decoded!.EventId.Should().Be(original.EventId);
      decoded.Channel.Should().Be(original.Channel);
      decoded.Properties["reason"].GetString().Should().Be("test");
    }
    finally { Directory.Delete(root, true); }
  }

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
    result.Should().Be(LogBatchWriteResult.Primary(1));
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

    result.Should().Be(new LogBatchWriteResult(0, 1, 0, true));
    sm.State.Should().Be(RecoveryState.Degraded);
  }
}
