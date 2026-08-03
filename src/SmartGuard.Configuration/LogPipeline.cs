using System.Collections.Concurrent;
using System.Threading.Channels;

namespace SmartGuard.Configuration;

public sealed class LogPipeline
{
  private readonly Channel<LogEvent> _normalQueue;
  private readonly Channel<LogEvent> _highQueue;
  private readonly Func<IReadOnlyList<LogEvent>, bool> _writeBatch;
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

  public LogPipeline(Func<IReadOnlyList<LogEvent>, bool> writeBatch, LogPipelineOptions? options = null)
  {
    _writeBatch = writeBatch ?? throw new ArgumentNullException(nameof(writeBatch));
    _options = options ?? new LogPipelineOptions();
    _normalQueue = Channel.CreateBounded<LogEvent>(CreateBoundedOptions(_options.NormalQueueCapacity));
    _highQueue = Channel.CreateBounded<LogEvent>(CreateBoundedOptions(_options.HighQueueCapacity));
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
      return;
    _consumerTask = Task.Run(ConsumeLoopAsync);
  }

  public bool TryEnqueue(LogPriority priority, LogEvent logEvent)
  {
    if (State is LogPipelineState.Flushing or LogPipelineState.Stopped)
    {
      CountDrop(priority);
      return false;
    }

    var queue = priority == LogPriority.High ? _highQueue : _normalQueue;
    if (queue.Writer.TryWrite(logEvent))
    {
      Interlocked.Increment(ref _enqueued);
      if (Interlocked.Increment(ref _pendingSinceFlush) >= _options.MaxBatchSize)
        _wakeSignal.Release();
      return true;
    }

    CountDrop(priority);
    return false;
  }

  public Task FlushAsync()
  {
    if (_consumerTask is null || State == LogPipelineState.Stopped)
    {
      FlushOnce();
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
    _healthTimer.Elapsed += (_, _) =>
    {
      try
      {
        var snapshot = health.TakeSnapshot(this);
        var logEvent = health.CreateHealthEvent(snapshot);
        TryEnqueue(LogPriority.High, logEvent);
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

    _wakeSignal.Release();
    var finished = await Task.WhenAny(consumer, Task.Delay(_options.ShutdownTimeoutMs))
      .ConfigureAwait(false) == consumer;
    if (!finished)
      Interlocked.Exchange(ref _state, (int)LogPipelineState.Stopped);
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
      while (_wakeSignal.Wait(0)) { }

      var waitersBeforeFlush = _flushWaiters.Count;
      Interlocked.Exchange(ref _pendingSinceFlush, 0);
      FlushOnce();
      CompleteFlushWaiters(waitersBeforeFlush);

      if (State is LogPipelineState.Flushing or LogPipelineState.Stopped)
      {
        FlushOnce();
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

  private List<LogEvent> TryFillBatch()
  {
    var batch = new List<LogEvent>(_options.MaxBatchSize);
    while (batch.Count < _options.MaxBatchSize && _highQueue.Reader.TryRead(out var high))
      batch.Add(high);
    while (batch.Count < _options.MaxBatchSize && _normalQueue.Reader.TryRead(out var normal))
      batch.Add(normal);
    return batch;
  }

  private static BoundedChannelOptions CreateBoundedOptions(int capacity) => new(capacity)
  {
    FullMode = BoundedChannelFullMode.Wait,
    SingleReader = true,
    SingleWriter = false,
  };
}
