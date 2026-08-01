using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogPipelineLifecycleTests
{
  private static LogPipelineOptions ManualOptions(
    int maxBatchSize = 200, int flushIntervalMs = 60_000, int shutdownTimeoutMs = 3_000) => new()
  {
    NormalQueueCapacity = 64,
    HighQueueCapacity = 64,
    MaxBatchSize = maxBatchSize,
    FlushIntervalMs = flushIntervalMs, // 默认拉满：只有 FlushAsync/凑批/StopAsync 能触发 flush
    ShutdownTimeoutMs = shutdownTimeoutMs,
  };

  [Fact]
  public void State_starts_in_starting_and_start_moves_to_running()
  {
    var pipeline = new LogPipeline(_ => true, ManualOptions());
    pipeline.State.Should().Be(LogPipelineState.Starting);
    pipeline.Start();
    pipeline.State.Should().Be(LogPipelineState.Running);
  }

  [Fact]
  public async Task Lines_enqueued_before_start_are_delivered_after_flush()
  {
    var written = new List<string>();
    var pipeline = new LogPipeline(
      batch => { lock (written) written.AddRange(batch); return true; }, ManualOptions());

    pipeline.TryEnqueue(LogPriority.Normal, "early").Should().BeTrue(
      "Starting 状态允许入队暂存，Start 后由消费者落盘");
    pipeline.Start();
    await pipeline.FlushAsync();

    lock (written) written.Should().Equal("early");
    (await pipeline.StopAsync()).Should().BeTrue();
  }

  [Fact]
  public async Task FlushAsync_delivers_all_enqueued_lines_without_waiting_interval()
  {
    var written = new List<string>();
    var pipeline = new LogPipeline(
      batch => { lock (written) written.AddRange(batch); return true; }, ManualOptions());
    pipeline.Start();

    pipeline.TryEnqueue(LogPriority.Normal, "a");
    pipeline.TryEnqueue(LogPriority.Normal, "b");
    await pipeline.FlushAsync();

    lock (written) written.Should().Equal("a", "b");
    (await pipeline.StopAsync()).Should().BeTrue();
  }

  [Fact]
  public async Task Reaching_max_batch_size_triggers_flush_without_timer()
  {
    var firstBatch = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    var pipeline = new LogPipeline(
      batch => { firstBatch.TrySetResult(batch.Count); return true; },
      ManualOptions(maxBatchSize: 2)); // FlushIntervalMs=60_000：只有凑满批能触发
    pipeline.Start();

    pipeline.TryEnqueue(LogPriority.Normal, "1");
    pipeline.TryEnqueue(LogPriority.Normal, "2");

    var done = await Task.WhenAny(firstBatch.Task, Task.Delay(5_000)); // Delay 仅失败保护
    done.Should().Be(firstBatch.Task, "凑满 MaxBatchSize 必须立即触发 flush，不得等定时器");
    (await firstBatch.Task).Should().Be(2);
    (await pipeline.StopAsync()).Should().BeTrue();
  }

  [Fact]
  public async Task Interval_timer_flushes_partial_batch_without_manual_trigger()
  {
    var firstBatch = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    var pipeline = new LogPipeline(
      batch => { firstBatch.TrySetResult(batch.Count); return true; },
      ManualOptions(flushIntervalMs: 50));
    pipeline.Start();

    pipeline.TryEnqueue(LogPriority.Normal, "solo"); // 不满一批，只能靠定时器兑底

    var done = await Task.WhenAny(firstBatch.Task, Task.Delay(5_000));
    done.Should().Be(firstBatch.Task, "FlushIntervalMs 到期必须兑底 flush 不满一批的行");
    (await firstBatch.Task).Should().Be(1);
    (await pipeline.StopAsync()).Should().BeTrue();
  }

  [Fact]
  public async Task StopAsync_drains_queue_rejects_new_lines_and_stops()
  {
    var written = new List<string>();
    var pipeline = new LogPipeline(
      batch => { lock (written) written.AddRange(batch); return true; }, ManualOptions());
    pipeline.Start();
    pipeline.TryEnqueue(LogPriority.Normal, "pending-1");
    pipeline.TryEnqueue(LogPriority.High, "pending-2");

    var stopped = await pipeline.StopAsync();

    stopped.Should().BeTrue();
    pipeline.State.Should().Be(LogPipelineState.Stopped);
    lock (written) written.Should().Contain("pending-1").And.Contain("pending-2");
    pipeline.TryEnqueue(LogPriority.Normal, "late").Should().BeFalse("Stopped 后必须拒收");
    pipeline.Diagnostics.DroppedNormal.Should().Be(1);
  }

  [Fact]
  public async Task StopAsync_returns_false_when_sink_blocks_beyond_timeout()
  {
    using var gate = new ManualResetEventSlim(false);
    var pipeline = new LogPipeline(
      _ => { gate.Wait(); return true; }, // sink 卡死，模拟磁盘 hang
      ManualOptions(maxBatchSize: 1, shutdownTimeoutMs: 200));
    pipeline.Start();
    try
    {
      pipeline.TryEnqueue(LogPriority.Normal, "stuck"); // maxBatchSize=1 立即触发 flush 并卡在 sink

      var stopped = await pipeline.StopAsync();

      stopped.Should().BeFalse("sink 卡死时 StopAsync 必须在 ShutdownTimeoutMs 内放弃并返回 false");
      pipeline.State.Should().Be(LogPipelineState.Stopped);
    }
    finally
    {
      gate.Set(); // 释放消费者任务，避免测试进程残留卡死线程
    }
  }

  [Fact]
  public async Task Start_is_idempotent()
  {
    var pipeline = new LogPipeline(_ => true, ManualOptions());
    pipeline.Start();
    pipeline.Start(); // 第二次为 no-op，不得再起消费者
    pipeline.State.Should().Be(LogPipelineState.Running);
    (await pipeline.StopAsync()).Should().BeTrue();
  }
}
