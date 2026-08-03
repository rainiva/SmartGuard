using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogPipelineLifecycleTests
{
  private static LogEvent Event(string message)
    => LogEventFactory.Create(
      LogSeverity.Info,
      LogComponentRegistry.Engine,
      LogChannelRegistry.Lifecycle,
      EventNameRegistry.AppStarted,
      message);

  private static LogPipelineOptions ManualOptions(
    int maxBatchSize = 200, int flushIntervalMs = 60_000, int shutdownTimeoutMs = 3_000) => new()
  {
    NormalQueueCapacity = 64,
    HighQueueCapacity = 64,
    MaxBatchSize = maxBatchSize,
    FlushIntervalMs = flushIntervalMs,
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
    var written = new List<LogEvent>();
    var pipeline = new LogPipeline(
      batch => { lock (written) written.AddRange(batch); return true; }, ManualOptions());

    pipeline.TryEnqueue(LogPriority.Normal, Event("early")).Should().BeTrue(
      "Starting 状态允许入队暂存，Start 后由消费者落盘");
    pipeline.Start();
    await pipeline.FlushAsync();

    lock (written) written.Select(e => e.Message).Should().Equal("early");
    (await pipeline.StopAsync()).Should().BeTrue();
  }

  [Fact]
  public async Task FlushAsync_delivers_all_enqueued_lines_without_waiting_interval()
  {
    var written = new List<LogEvent>();
    var pipeline = new LogPipeline(
      batch => { lock (written) written.AddRange(batch); return true; }, ManualOptions());
    pipeline.Start();

    pipeline.TryEnqueue(LogPriority.Normal, Event("a"));
    pipeline.TryEnqueue(LogPriority.Normal, Event("b"));
    await pipeline.FlushAsync();

    lock (written) written.Select(e => e.Message).Should().Equal("a", "b");
    (await pipeline.StopAsync()).Should().BeTrue();
  }

  [Fact]
  public async Task Reaching_max_batch_size_triggers_flush_without_timer()
  {
    var firstBatch = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    var pipeline = new LogPipeline(
      batch => { firstBatch.TrySetResult(batch.Count); return true; },
      ManualOptions(maxBatchSize: 2));
    pipeline.Start();

    pipeline.TryEnqueue(LogPriority.Normal, Event("1"));
    pipeline.TryEnqueue(LogPriority.Normal, Event("2"));

    var done = await Task.WhenAny(firstBatch.Task, Task.Delay(5_000));
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

    pipeline.TryEnqueue(LogPriority.Normal, Event("solo"));

    var done = await Task.WhenAny(firstBatch.Task, Task.Delay(5_000));
    done.Should().Be(firstBatch.Task, "FlushIntervalMs 到期必须兑底 flush 不满一批的行");
    (await firstBatch.Task).Should().Be(1);
    (await pipeline.StopAsync()).Should().BeTrue();
  }

  [Fact]
  public async Task StopAsync_drains_queue_rejects_new_lines_and_stops()
  {
    var written = new List<LogEvent>();
    var pipeline = new LogPipeline(
      batch => { lock (written) written.AddRange(batch); return true; }, ManualOptions());
    pipeline.Start();
    pipeline.TryEnqueue(LogPriority.Normal, Event("pending-1"));
    pipeline.TryEnqueue(LogPriority.High, Event("pending-2"));

    var stopped = await pipeline.StopAsync();

    stopped.Should().BeTrue();
    pipeline.State.Should().Be(LogPipelineState.Stopped);
    lock (written) written.Select(e => e.Message).Should().Contain(new[] { "pending-1", "pending-2" });
    pipeline.TryEnqueue(LogPriority.Normal, Event("late")).Should().BeFalse("Stopped 后必须拒收");
    pipeline.Diagnostics.DroppedNormal.Should().Be(1);
  }

  [Fact]
  public async Task StopAsync_returns_false_when_sink_blocks_beyond_timeout()
  {
    using var gate = new ManualResetEventSlim(false);
    var pipeline = new LogPipeline(
      _ => { gate.Wait(); return true; },
      ManualOptions(maxBatchSize: 1, shutdownTimeoutMs: 200));
    pipeline.Start();
    try
    {
      pipeline.TryEnqueue(LogPriority.Normal, Event("stuck"));

      var stopped = await pipeline.StopAsync();

      stopped.Should().BeFalse("sink 卡死时 StopAsync 必须在 ShutdownTimeoutMs 内放弃并返回 false");
      pipeline.State.Should().Be(LogPipelineState.Stopped);
    }
    finally
    {
      gate.Set();
    }
  }

  [Fact]
  public async Task Start_is_idempotent()
  {
    var pipeline = new LogPipeline(_ => true, ManualOptions());
    pipeline.Start();
    pipeline.Start();
    pipeline.State.Should().Be(LogPipelineState.Running);
    (await pipeline.StopAsync()).Should().BeTrue();
  }
}
