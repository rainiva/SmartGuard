using System.Collections.Concurrent;
using System.Threading.Channels;

namespace SmartGuard.Configuration;

public sealed class LogPipeline
{
  private readonly Channel<string> _normalQueue;
  private readonly Channel<string> _highQueue;
  private readonly Func<IReadOnlyList<string>, bool> _writeBatch;
  private readonly LogPipelineOptions _options;
  private readonly SemaphoreSlim _wakeSignal = new(0, int.MaxValue);
  private readonly ConcurrentQueue<TaskCompletionSource<bool>> _flushWaiters = new();
  private Task? _consumerTask;
  private int _state = (int)LogPipelineState.Starting;
  private int _pendingSinceFlush;
  private long _enqueued;
  private long _droppedNormal;
  private long _droppedHigh;
  private long _writtenLines;
  private long _failedBatches;
  private System.Timers.Timer? _healthTimer;

  public LogPipeline(Func<IReadOnlyList<string>, bool> writeBatch, LogPipelineOptions? options = null)
  {
    _writeBatch = writeBatch ?? throw new ArgumentNullException(nameof(writeBatch));
    _options = options ?? new LogPipelineOptions();
    _normalQueue = Channel.CreateBounded<string>(CreateBoundedOptions(_options.NormalQueueCapacity));
    _highQueue = Channel.CreateBounded<string>(CreateBoundedOptions(_options.HighQueueCapacity));
  }

  public LogPipelineState State => (LogPipelineState)Volatile.Read(ref _state);

  public int NormalQueueDepth => _normalQueue.Reader.Count;
  public int HighQueueDepth => _highQueue.Reader.Count;

  public LogPipelineDiagnostics Diagnostics => new(
    Interlocked.Read(ref _enqueued),
    Interlocked.Read(ref _droppedNormal),
    Interlocked.Read(ref _droppedHigh),
    Interlocked.Read(ref _writtenLines),
    Interlocked.Read(ref _failedBatches));

  public void Start()
  {
    if (Interlocked.CompareExchange(
          ref _state, (int)LogPipelineState.Running, (int)LogPipelineState.Starting)
        != (int)LogPipelineState.Starting)
      return; // 幂等：仅 Starting→Running 起一次消费者
    _consumerTask = Task.Run(ConsumeLoopAsync);
  }

  public bool TryEnqueue(LogPriority priority, string line)
  {
    if (State is LogPipelineState.Flushing or LogPipelineState.Stopped)
    {
      CountDrop(priority);
      return false; // 关闭中/已关闭：停止接收
    }

    var queue = priority == LogPriority.High ? _highQueue : _normalQueue;
    if (queue.Writer.TryWrite(line))
    {
      Interlocked.Increment(ref _enqueued);
      if (Interlocked.Increment(ref _pendingSinceFlush) >= _options.MaxBatchSize)
        _wakeSignal.Release(); // 凑满一批立即唤醒消费者，不等定时器
      return true;
    }

    // FullMode.Wait + TryWrite：队列满时 TryWrite 返回 false，可精确计数丢弃；
    // 禁用 DropWrite（其 TryWrite 恒 true，静默丢行且无法计数）。
    CountDrop(priority);
    return false;
  }

  public Task FlushAsync()
  {
    if (_consumerTask is null || State == LogPipelineState.Stopped)
    {
      FlushOnce(); // 无消费者线程：同步兑底
      return Task.CompletedTask;
    }

    var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    _flushWaiters.Enqueue(waiter);
    _wakeSignal.Release();
    return waiter.Task;
  }

  public void StartHealthReporting(LogPipelineHealth health, TimeSpan interval)
  {
    StopHealthReporting();
    _healthTimer = new System.Timers.Timer(interval.TotalMilliseconds)
    {
      AutoReset = true,
      Enabled = true
    };
    _healthTimer.Elapsed += (s, e) =>
    {
      try
      {
        var snapshot = health.TakeSnapshot(this);
        var jsonLine = health.FormatJson(snapshot);
        TryEnqueue(LogPriority.High, jsonLine);
      }
      catch
      {
        // 健康报告异常绝不影响管线主路径
      }
    };
  }

  public void StopHealthReporting()
  {
    if (_healthTimer != null)
    {
      _healthTimer.Enabled = false;
      _healthTimer.Dispose();
      _healthTimer = null;
    }
  }

  public async Task<bool> StopAsync()
  {
    StopHealthReporting();
    // Running→Flushing（停止接收）；Starting→Stopped（从未启动，同步排空）
    Interlocked.CompareExchange(
      ref _state, (int)LogPipelineState.Flushing, (int)LogPipelineState.Running);
    Interlocked.CompareExchange(
      ref _state, (int)LogPipelineState.Stopped, (int)LogPipelineState.Starting);

    var consumer = _consumerTask;
    if (consumer is null)
    {
      FlushOnce();
      CompleteFlushWaiters(int.MaxValue);
      Volatile.Write(ref _state, (int)LogPipelineState.Stopped);
      return true;
    }

    _wakeSignal.Release(); // 唤醒消费者进入收尾分支
    var finished = await Task.WhenAny(consumer, Task.Delay(_options.ShutdownTimeoutMs))
      .ConfigureAwait(false) == consumer;
    if (!finished)
      Interlocked.Exchange(ref _state, (int)LogPipelineState.Stopped); // 超时：置 Stopped，消费者随后自行收敛退出
    return finished;
  }

  public bool FlushOnce()
  {
    var allOk = true;
    while (true)
    {
      var batch = TryFillBatch();
      if (batch.Count == 0) return allOk;
      if (_writeBatch(batch))
      {
        Interlocked.Add(ref _writtenLines, batch.Count);
      }
      else
      {
        Interlocked.Increment(ref _failedBatches);
        allOk = false;
      }
    }
  }

  private async Task ConsumeLoopAsync()
  {
    while (true)
    {
      await _wakeSignal.WaitAsync(TimeSpan.FromMilliseconds(_options.FlushIntervalMs))
        .ConfigureAwait(false);
      while (_wakeSignal.Wait(0)) { } // 合并积压唤醒，一次 flush 处理

      var waitersBeforeFlush = _flushWaiters.Count; // 快照：只完成 flush 前已登记的等待者
      Interlocked.Exchange(ref _pendingSinceFlush, 0);
      FlushOnce();
      CompleteFlushWaiters(waitersBeforeFlush);

      if (State is LogPipelineState.Flushing or LogPipelineState.Stopped)
      {
        // 含 Stopped：StopAsync 超时后消费者也必须自行收敛退出，避免任务泄漏
        FlushOnce(); // 收尾：把停止接收前最后入队的行也排空
        Volatile.Write(ref _state, (int)LogPipelineState.Stopped);
        CompleteFlushWaiters(int.MaxValue);
        return;
      }
    }
  }

  private void CompleteFlushWaiters(int upTo)
  {
    for (var i = 0; i < upTo && _flushWaiters.TryDequeue(out var waiter); i++)
      waiter.TrySetResult(true);
  }

  private void CountDrop(LogPriority priority)
  {
    if (priority == LogPriority.High) Interlocked.Increment(ref _droppedHigh);
    else Interlocked.Increment(ref _droppedNormal);
  }

  private List<string> TryFillBatch()
  {
    var batch = new List<string>(_options.MaxBatchSize);
    while (batch.Count < _options.MaxBatchSize && _highQueue.Reader.TryRead(out var high))
      batch.Add(high);
    while (batch.Count < _options.MaxBatchSize && _normalQueue.Reader.TryRead(out var normal))
      batch.Add(normal);
    return batch;
  }

  private static BoundedChannelOptions CreateBoundedOptions(int capacity) => new(capacity)
  {
    FullMode = BoundedChannelFullMode.Wait, // 满时 TryWrite=false，由调用方计数丢弃
    SingleReader = true,  // 唯一后台消费者
    SingleWriter = false, // 业务线程多写者
  };
}
