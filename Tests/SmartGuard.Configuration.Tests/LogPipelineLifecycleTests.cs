using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogPipelineLifecycleTests
{
  [Fact]
  public async Task Flush_cannot_observe_loss_as_history_before_its_record_is_processed()
  {
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    using var returning = new ManualResetEventSlim();
    var pipeline = new LogPipeline(batch =>
    {
      entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); returning.Set();
      return LogBatchWriteResult.Failed(batch.Count);
    }, ManualOptions(maxBatchSize: 1));
    pipeline.Start();
    pipeline.TryEnqueue(LogPriority.Normal, Event("lost-in-flight"));
    entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
    var gate = typeof(LogPipeline).GetField("_gate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(pipeline)!;
    Task<bool> flush;
    Monitor.Enter(gate);
    try
    {
      release.Set();
      returning.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
      // Hold watermark publication. The old consumer publishes loss first,
      // allowing a request to mistake this still-pending loss for history.
      SpinWait.SpinUntil(() => pipeline.Diagnostics.LostLines == 1, 100);
      flush = pipeline.FlushAsync();
    }
    finally { Monitor.Exit(gate); release.Set(); }
    (await flush).Should().BeFalse();
    await pipeline.StopAsync();
  }

  [Fact]
  public async Task Stop_waits_for_the_cold_reader_to_transfer_ownership()
  {
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var pipeline = new LogPipeline(batch =>
    {
      entered.Set();
      release.Wait(TimeSpan.FromSeconds(5));
      return LogBatchWriteResult.Primary(batch.Count);
    }, ManualOptions());
    pipeline.TryEnqueue(LogPriority.Normal, Event("cold"));
    var cold = Task.Run(pipeline.FlushOnce);
    entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
    var stop = pipeline.StopAsync();
    try
    {
      await Task.Delay(50);
      stop.IsCompleted.Should().BeFalse("the cold reader still owns an accepted record");
      release.Set();
      (await cold).Should().BeTrue();
      (await stop).Should().BeTrue();
      pipeline.State.Should().Be(LogPipelineState.Stopped);
    }
    finally { release.Set(); await cold; await pipeline.StopAsync(); }
  }

  [Fact]
  public async Task Flush_confirms_its_frozen_range_without_waiting_for_later_records()
  {
    using var firstEntered = new ManualResetEventSlim();
    using var laterEntered = new ManualResetEventSlim();
    using var firstRelease = new ManualResetEventSlim();
    using var laterRelease = new ManualResetEventSlim();
    var pipeline = new LogPipeline(batch =>
    {
      if (batch[0].Message == "first") { firstEntered.Set(); firstRelease.Wait(TimeSpan.FromSeconds(5)); }
      else { laterEntered.Set(); laterRelease.Wait(TimeSpan.FromSeconds(5)); }
      return LogBatchWriteResult.Primary(batch.Count);
    }, ManualOptions(maxBatchSize: 1));
    pipeline.Start();
    pipeline.TryEnqueue(LogPriority.Normal, Event("first"));
    firstEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
    var flush = pipeline.FlushAsync();
    pipeline.TryEnqueue(LogPriority.Normal, Event("later"));
    try
    {
      firstRelease.Set();
      laterEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
      (await flush.WaitAsync(TimeSpan.FromSeconds(1))).Should().BeTrue();
    }
    finally { firstRelease.Set(); laterRelease.Set(); await pipeline.StopAsync(); }
  }

  [Fact]
  public async Task Cold_async_flush_returns_a_task_while_the_writer_is_blocked()
  {
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var pipeline = new LogPipeline(batch =>
    {
      entered.Set();
      release.Wait(TimeSpan.FromSeconds(5));
      return LogBatchWriteResult.Primary(batch.Count);
    }, ManualOptions());
    pipeline.TryEnqueue(LogPriority.Normal, Event("cold"));
    var caller = Task.Run(async () =>
    {
      var operation = pipeline.FlushAsync();
      returned.SetResult();
      await operation;
    });
    try
    {
      entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
      await returned.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }
    finally
    {
      release.Set();
      await caller;
      await pipeline.StopAsync();
    }
  }

  [Fact]
  public async Task Cancellation_is_caller_local_and_late_writer_keeps_resource_ownership()
  {
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var pipeline = new LogPipeline(batch =>
    {
      entered.Set();
      release.Wait(TimeSpan.FromSeconds(5));
      return LogBatchWriteResult.Primary(batch.Count);
    }, ManualOptions(maxBatchSize: 1, shutdownTimeoutMs: 100));
    pipeline.Start();
    pipeline.TryEnqueue(LogPriority.High, Event("owned"));
    entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
    using var cancelled = new CancellationTokenSource();
    var cancelledWait = pipeline.FlushAsync(cancelled.Token);
    var otherWait = pipeline.FlushAsync();
    try
    {
      cancelled.Cancel();
      await ((Func<Task>)(async () => await cancelledWait)).Should().ThrowAsync<OperationCanceledException>();
      (await pipeline.StopAsync()).Should().BeFalse();
      release.Set();
      // The timeout does not dispose resources still owned by the late consumer.
      await otherWait;
      var consumer = (Task)typeof(LogPipeline).GetField("_consumerTask", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(pipeline)!;
      await consumer.WaitAsync(TimeSpan.FromSeconds(2));
      pipeline.Diagnostics.WrittenLines.Should().Be(1);
      (await pipeline.StopAsync()).Should().BeFalse("the previously reported stop timeout is not silently rewritten");
    }
    finally { release.Set(); await pipeline.StopAsync(); }
  }

  [Fact]
  public async Task Flush_confirms_failure_instead_of_only_signalling_completion()
  {
    var pipeline = new LogPipeline(batch => LogBatchWriteResult.Failed(batch.Count), ManualOptions());
    pipeline.Start();
    pipeline.TryEnqueue(LogPriority.Normal, Event("lost"));
    var flush = pipeline.FlushAsync();
    flush.Should().BeAssignableTo<Task<bool>>();
    (await (Task<bool>)flush).Should().BeFalse();
    await pipeline.StopAsync();
  }

  [Fact]
  public async Task Flush_once_cannot_compete_with_running_single_reader()
  {
    var calls = 0;
    var pipeline = new LogPipeline(batch => { Interlocked.Increment(ref calls); return LogBatchWriteResult.Primary(batch.Count); }, ManualOptions());
    pipeline.Start();
    pipeline.TryEnqueue(LogPriority.Normal, Event("owned"));
    pipeline.FlushOnce().Should().BeFalse("only the running consumer owns queue reads");
    Volatile.Read(ref calls).Should().Be(0);
    await pipeline.StopAsync();
  }

  [Fact]
  public async Task Thousand_flush_requests_keep_at_most_two_internal_generations()
  {
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var pipeline = new LogPipeline(batch =>
    {
      entered.Set();
      release.Wait(TimeSpan.FromSeconds(5));
      return LogBatchWriteResult.Primary(batch.Count);
    }, ManualOptions(maxBatchSize: 1));
    pipeline.Start();
    pipeline.TryEnqueue(LogPriority.Normal, Event("blocked"));
    try
    {
      entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
      var requests = Enumerable.Range(0, 1_000).Select(_ => pipeline.FlushAsync()).ToArray();
      var metric = typeof(LogPipeline).GetProperty("PendingFlushGenerations",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
      metric.Should().NotBeNull("internal resource usage must be measurable without per-caller waiter storage");
      ((int)metric!.GetValue(pipeline)!).Should().BeInRange(1, 2);
      release.Set();
      await Task.WhenAll(requests);
    }
    finally
    {
      release.Set();
      await pipeline.StopAsync();
    }
  }

  [Fact]
  public async Task Stop_must_not_report_callback_failure_as_successful_drain()
  {
    var pipeline = new LogPipeline(_ => throw new IOException("writer fault"), ManualOptions());
    pipeline.Start();
    pipeline.TryEnqueue(LogPriority.Normal, Event("unconfirmed"));
    (await pipeline.StopAsync()).Should().BeFalse();
    pipeline.Diagnostics.WrittenLines.Should().Be(0);
    pipeline.Diagnostics.FailedBatches.Should().Be(1);
    using var health = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(pipeline.Diagnostics));
    health.RootElement.GetProperty("ConsumerFailed").GetBoolean().Should().BeTrue();
    health.RootElement.GetProperty("StopFailed").GetBoolean().Should().BeTrue();
  }

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
    var pipeline = new LogPipeline(batch => LogBatchWriteResult.Primary(batch.Count), ManualOptions());
    pipeline.State.Should().Be(LogPipelineState.Starting);
    pipeline.Start();
    pipeline.State.Should().Be(LogPipelineState.Running);
  }

  [Fact]
  public async Task Lines_enqueued_before_start_are_delivered_after_flush()
  {
    var written = new List<LogEvent>();
    var pipeline = new LogPipeline(
      batch => { lock (written) written.AddRange(batch); return LogBatchWriteResult.Primary(batch.Count); }, ManualOptions());

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
      batch => { lock (written) written.AddRange(batch); return LogBatchWriteResult.Primary(batch.Count); }, ManualOptions());
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
      batch => { firstBatch.TrySetResult(batch.Count); return LogBatchWriteResult.Primary(batch.Count); },
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
      batch => { firstBatch.TrySetResult(batch.Count); return LogBatchWriteResult.Primary(batch.Count); },
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
      batch => { lock (written) written.AddRange(batch); return LogBatchWriteResult.Primary(batch.Count); }, ManualOptions());
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
      batch => { gate.Wait(); return LogBatchWriteResult.Primary(batch.Count); },
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
    var pipeline = new LogPipeline(batch => LogBatchWriteResult.Primary(batch.Count), ManualOptions());
    pipeline.Start();
    pipeline.Start();
    pipeline.State.Should().Be(LogPipelineState.Running);
    (await pipeline.StopAsync()).Should().BeTrue();
  }
}
