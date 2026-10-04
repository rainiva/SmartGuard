using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogPipelineHealthTests
{
  [Fact]
  public void Unmeasured_latency_is_null_and_health_reports_the_write_partition()
  {
    var pipeline = new LogPipeline(batch => new LogBatchWriteResult(0, batch.Count, 0, true));
    pipeline.TryEnqueue(LogPriority.Normal, Event("fallback"));
    pipeline.FlushOnce().Should().BeTrue();
    pipeline.RecordBackpressureDrop();
    var health = new LogPipelineHealth();
    var snapshot = health.TakeSnapshot(pipeline);
    using var json = System.Text.Json.JsonDocument.Parse(health.FormatJson(snapshot));
    json.RootElement.GetProperty("averageLatencyMs").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    json.RootElement.GetProperty("p99LatencyMs").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    json.RootElement.GetProperty("fallbackWrittenLines").GetInt64().Should().Be(1);
    json.RootElement.GetProperty("primaryFailedBatches").GetInt64().Should().Be(1);
    json.RootElement.GetProperty("lostLines").GetInt64().Should().Be(0);
    health.CreateHealthEvent(snapshot).Properties!["droppedByBackpressure"].GetInt64().Should().Be(1);
  }

  [Fact]
  public void Health_sample_identifies_the_process_whose_counters_are_reported()
  {
    var health = new LogPipelineHealth();
    var sample = health.CreateHealthEvent(new LogHealthSnapshot(1,0,0,1,0,0,0,0,0,0,0));
    sample.ProcessId.Should().Be(Environment.ProcessId);
  }
  private static LogEvent Event(string message)
    => LogEventFactory.Create(
      LogSeverity.Info,
      LogComponentRegistry.Engine,
      LogChannelRegistry.Lifecycle,
      EventNameRegistry.AppStarted,
      message);

  [Fact]
  public void TakeSnapshot_captures_current_diagnostics()
  {
    var pipeline = new LogPipeline(batch => LogBatchWriteResult.Primary(batch.Count));
    pipeline.Start();
    pipeline.TryEnqueue(LogPriority.Normal, Event("line1"));
    pipeline.TryEnqueue(LogPriority.High, Event("line2"));
    pipeline.FlushAsync().GetAwaiter().GetResult();

    var health = new LogPipelineHealth();
    var snapshot = health.TakeSnapshot(pipeline);

    snapshot.EnqueuedTotal.Should().BeGreaterThanOrEqualTo(2);
    snapshot.WrittenLinesTotal.Should().BeGreaterThanOrEqualTo(2);
    snapshot.DroppedNormal.Should().Be(0);
    snapshot.DroppedHigh.Should().Be(0);

    pipeline.StopAsync().GetAwaiter().GetResult();
  }

  [Fact]
  public void FormatJson_produces_valid_json_line()
  {
    var health = new LogPipelineHealth();
    var snapshot = new LogHealthSnapshot(
      EnqueuedTotal: 100,
      DroppedNormal: 5,
      DroppedHigh: 0,
      WrittenLinesTotal: 95,
      FailedBatches: 1,
      NormalQueueDepth: 10,
      HighQueueDepth: 0,
      EnqueueRatePerSec: 25.0,
      WriteRatePerSec: 23.5,
      AverageLatencyMs: 50.0,
      P99LatencyMs: 150.0);

    var json = health.FormatJson(snapshot);
    json.Should().NotBeNullOrEmpty();
    json.Should().Contain("\"enqueuedTotal\"");
    json.Should().Contain("\"droppedNormal\"");
    json.Should().Contain("\"p99LatencyMs\"");
    json.Should().NotContain("\n");
  }

  [Fact]
  public void TakeSnapshot_computes_rates_from_two_snapshots()
  {
    var pipeline = new LogPipeline(batch => LogBatchWriteResult.Primary(batch.Count));
    pipeline.Start();

    var health = new LogPipelineHealth();
    var snap1 = health.TakeSnapshot(pipeline);

    for (var i = 0; i < 10; i++)
      pipeline.TryEnqueue(LogPriority.Normal, Event($"line-{i}"));
    pipeline.FlushAsync().GetAwaiter().GetResult();

    var snap2 = health.TakeSnapshot(pipeline);
    snap2.EnqueuedTotal.Should().BeGreaterThan(snap1.EnqueuedTotal);

    pipeline.StopAsync().GetAwaiter().GetResult();
  }

  [Fact]
  public async Task StartHealthReporting_emits_structured_health_event_after_interval()
  {
    var writtenEvents = new System.Collections.Concurrent.ConcurrentQueue<LogEvent>();
    var pipeline = new LogPipeline(batch =>
    {
      foreach (var logEvent in batch)
        writtenEvents.Enqueue(logEvent);
      return LogBatchWriteResult.Primary(batch.Count);
    });
    pipeline.Start();

    var health = new LogPipelineHealth();
    pipeline.StartHealthReporting(health, TimeSpan.FromMilliseconds(100));

    await Task.Delay(1000);

    pipeline.StopHealthReporting();
    pipeline.StopAsync().GetAwaiter().GetResult();

    pipeline.Diagnostics.WrittenLines.Should().BeGreaterThanOrEqualTo(2);
    writtenEvents.Should().Contain(logEvent =>
      logEvent.EventName == EventNameRegistry.PipelineHealthSampled
      && logEvent.Component == LogComponentRegistry.Configuration
      && logEvent.Channel == LogChannelRegistry.Diagnostics);
  }

  [Fact]
  public async Task StopHealthReporting_halts_emission()
  {
    var pipeline = new LogPipeline(batch => LogBatchWriteResult.Primary(batch.Count));
    pipeline.Start();

    var health = new LogPipelineHealth();
    pipeline.StartHealthReporting(health, TimeSpan.FromMilliseconds(100));
    await Task.Delay(300);

    var enqueuedBeforeStop = pipeline.Diagnostics.Enqueued;
    pipeline.StopHealthReporting();

    await Task.Delay(500);

    var enqueuedAfterStop = pipeline.Diagnostics.Enqueued;
    (enqueuedAfterStop - enqueuedBeforeStop).Should().BeLessThanOrEqualTo(1);

    pipeline.StopAsync().GetAwaiter().GetResult();
  }
}
