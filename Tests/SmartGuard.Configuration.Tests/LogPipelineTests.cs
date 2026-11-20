using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogPipelineTests
{
  [Fact]
  public async Task Invalid_partition_is_unconfirmed_and_does_not_silently_stop_consumption()
  {
    var calls = 0;
    var pipeline = new LogPipeline(batch => ++calls == 1
      ? new LogBatchWriteResult(batch.Count + 1, 0, 0, false)
      : LogBatchWriteResult.Primary(batch.Count));
    pipeline.TryEnqueue(LogPriority.Normal, Event("invalid"));
    pipeline.FlushOnce().Should().BeFalse();
    pipeline.TryEnqueue(LogPriority.Normal, Event("next"));
    pipeline.FlushOnce().Should().BeTrue();
    pipeline.Diagnostics.LostLines.Should().Be(1);
    pipeline.Diagnostics.PrimaryWrittenLines.Should().Be(1);
    pipeline.Diagnostics.WrittenLines.Should().Be(1);
    (await pipeline.StopAsync()).Should().BeFalse("a consumer callback fault must remain observable");
  }

  private static LogEvent Event(string message)
    => LogEventFactory.Create(
      LogSeverity.Info,
      LogComponentRegistry.Engine,
      LogChannelRegistry.Lifecycle,
      EventNameRegistry.AppStarted,
      message);

  private static LogPipelineOptions TinyOptions(
    int normalCapacity = 8, int highCapacity = 8, int maxBatchSize = 200) => new()
  {
    NormalQueueCapacity = normalCapacity,
    HighQueueCapacity = highCapacity,
    MaxBatchSize = maxBatchSize,
    FlushIntervalMs = 60_000, // 本文件全部手动 FlushOnce，定时器不参与
  };

  [Fact]
  public void FlushOnce_delivers_lines_in_enqueue_order()
  {
    var written = new List<LogEvent>();
    var pipeline = new LogPipeline(batch => { written.AddRange(batch); return LogBatchWriteResult.Primary(batch.Count); }, TinyOptions());

    pipeline.TryEnqueue(LogPriority.Normal, Event("a")).Should().BeTrue();
    pipeline.TryEnqueue(LogPriority.Normal, Event("b")).Should().BeTrue();
    pipeline.FlushOnce().Should().BeTrue();

    written.Select(e => e.Message).Should().Equal("a", "b");
  }

  [Fact]
  public void FlushOnce_drains_high_priority_before_normal()
  {
    var written = new List<LogEvent>();
    var pipeline = new LogPipeline(batch => { written.AddRange(batch); return LogBatchWriteResult.Primary(batch.Count); }, TinyOptions());

    pipeline.TryEnqueue(LogPriority.Normal, Event("normal-1"));
    pipeline.TryEnqueue(LogPriority.High, Event("high-1"));
    pipeline.TryEnqueue(LogPriority.Normal, Event("normal-2"));
    pipeline.FlushOnce();

    written.Select(e => e.Message).Should().Equal("high-1", "normal-1", "normal-2");
  }

  [Fact]
  public void TryEnqueue_counts_dropped_normal_when_normal_queue_full()
  {
    var pipeline = new LogPipeline(batch => LogBatchWriteResult.Primary(batch.Count), TinyOptions(normalCapacity: 2));

    pipeline.TryEnqueue(LogPriority.Normal, Event("1")).Should().BeTrue();
    pipeline.TryEnqueue(LogPriority.Normal, Event("2")).Should().BeTrue();
    pipeline.TryEnqueue(LogPriority.Normal, Event("3")).Should().BeFalse("队列满时必须非阻塞拒绝，不得阻塞业务线程");

    pipeline.Diagnostics.DroppedNormal.Should().Be(1);
    pipeline.Diagnostics.Enqueued.Should().Be(2);
  }

  [Fact]
  public void TryEnqueue_counts_dropped_high_when_high_queue_full()
  {
    var pipeline = new LogPipeline(batch => LogBatchWriteResult.Primary(batch.Count), TinyOptions(highCapacity: 1));

    pipeline.TryEnqueue(LogPriority.High, Event("1")).Should().BeTrue();
    pipeline.TryEnqueue(LogPriority.High, Event("2")).Should().BeFalse();

    pipeline.Diagnostics.DroppedHigh.Should().Be(1);
  }

  [Fact]
  public void FlushOnce_splits_output_by_max_batch_size()
  {
    var batchSizes = new List<int>();
    var pipeline = new LogPipeline(
      batch => { batchSizes.Add(batch.Count); return LogBatchWriteResult.Primary(batch.Count); }, TinyOptions(maxBatchSize: 2));

    for (var i = 0; i < 5; i++)
      pipeline.TryEnqueue(LogPriority.Normal, Event($"line-{i}"));
    pipeline.FlushOnce();

    batchSizes.Should().Equal(2, 2, 1);
  }

  [Fact]
  public void FlushOnce_counts_failed_batches_and_returns_false_when_sink_fails()
  {
    var pipeline = new LogPipeline(batch => LogBatchWriteResult.Failed(batch.Count), TinyOptions());

    pipeline.TryEnqueue(LogPriority.Normal, Event("doomed"));
    pipeline.FlushOnce().Should().BeFalse();

    pipeline.Diagnostics.FailedBatches.Should().Be(1);
    pipeline.Diagnostics.WrittenLines.Should().Be(0);
  }

  [Fact]
  public void Diagnostics_counts_written_lines_on_success()
  {
    var pipeline = new LogPipeline(batch => LogBatchWriteResult.Primary(batch.Count), TinyOptions());

    pipeline.TryEnqueue(LogPriority.Normal, Event("1"));
    pipeline.TryEnqueue(LogPriority.High, Event("2"));
    pipeline.FlushOnce();

    pipeline.Diagnostics.WrittenLines.Should().Be(2);
  }
}
