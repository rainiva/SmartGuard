using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogPipelineTests
{
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
    var written = new List<string>();
    var pipeline = new LogPipeline(batch => { written.AddRange(batch); return true; }, TinyOptions());

    pipeline.TryEnqueue(LogPriority.Normal, "a").Should().BeTrue();
    pipeline.TryEnqueue(LogPriority.Normal, "b").Should().BeTrue();
    pipeline.FlushOnce().Should().BeTrue();

    written.Should().Equal("a", "b");
  }

  [Fact]
  public void FlushOnce_drains_high_priority_before_normal()
  {
    var written = new List<string>();
    var pipeline = new LogPipeline(batch => { written.AddRange(batch); return true; }, TinyOptions());

    pipeline.TryEnqueue(LogPriority.Normal, "normal-1");
    pipeline.TryEnqueue(LogPriority.High, "high-1");
    pipeline.TryEnqueue(LogPriority.Normal, "normal-2");
    pipeline.FlushOnce();

    written.Should().Equal("high-1", "normal-1", "normal-2");
  }

  [Fact]
  public void TryEnqueue_counts_dropped_normal_when_normal_queue_full()
  {
    var pipeline = new LogPipeline(_ => true, TinyOptions(normalCapacity: 2));

    pipeline.TryEnqueue(LogPriority.Normal, "1").Should().BeTrue();
    pipeline.TryEnqueue(LogPriority.Normal, "2").Should().BeTrue();
    pipeline.TryEnqueue(LogPriority.Normal, "3").Should().BeFalse("队列满时必须非阻塞拒绝，不得阻塞业务线程");

    pipeline.Diagnostics.DroppedNormal.Should().Be(1);
    pipeline.Diagnostics.Enqueued.Should().Be(2);
  }

  [Fact]
  public void TryEnqueue_counts_dropped_high_when_high_queue_full()
  {
    var pipeline = new LogPipeline(_ => true, TinyOptions(highCapacity: 1));

    pipeline.TryEnqueue(LogPriority.High, "1").Should().BeTrue();
    pipeline.TryEnqueue(LogPriority.High, "2").Should().BeFalse();

    pipeline.Diagnostics.DroppedHigh.Should().Be(1);
  }

  [Fact]
  public void FlushOnce_splits_output_by_max_batch_size()
  {
    var batchSizes = new List<int>();
    var pipeline = new LogPipeline(
      batch => { batchSizes.Add(batch.Count); return true; }, TinyOptions(maxBatchSize: 2));

    for (var i = 0; i < 5; i++)
      pipeline.TryEnqueue(LogPriority.Normal, $"line-{i}");
    pipeline.FlushOnce();

    batchSizes.Should().Equal(2, 2, 1);
  }

  [Fact]
  public void FlushOnce_counts_failed_batches_and_returns_false_when_sink_fails()
  {
    var pipeline = new LogPipeline(_ => false, TinyOptions());

    pipeline.TryEnqueue(LogPriority.Normal, "doomed");
    pipeline.FlushOnce().Should().BeFalse();

    pipeline.Diagnostics.FailedBatches.Should().Be(1);
    pipeline.Diagnostics.WrittenLines.Should().Be(0);
  }

  [Fact]
  public void Diagnostics_counts_written_lines_on_success()
  {
    var pipeline = new LogPipeline(_ => true, TinyOptions());

    pipeline.TryEnqueue(LogPriority.Normal, "1");
    pipeline.TryEnqueue(LogPriority.High, "2");
    pipeline.FlushOnce();

    pipeline.Diagnostics.WrittenLines.Should().Be(2);
  }
}
