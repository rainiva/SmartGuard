using System.Threading.Channels;

namespace SmartGuard.Configuration;

public sealed class LogPipeline
{
  private readonly Channel<LogEvent> _normalQueue;
  private readonly Channel<LogEvent> _highQueue;
  private readonly Func<IReadOnlyList<LogEvent>, LogBatchWriteResult> _writeBatch;
  private readonly LogPipelineOptions _options;
  private readonly SemaphoreSlim _wakeSignal = new(0, 1);
  // Short memory-only critical section: admission, watermarks and ownership.
  // No writer callback or asynchronous wait is ever executed under this lock.
  private readonly object _gate = new();
  private FlushGeneration? _activeFlush;
  private FlushGeneration? _nextFlush;
  private Task<bool>? _consumerTask;
  private TaskCompletionSource? _coldReleased;
  private long _acceptedNormal, _acceptedHigh, _processedNormal, _processedHigh;
  private long _stopNormal, _stopHigh, _stopLostBaseline;
  private bool _stopRequested, _stopTimedOut, _coldDraining, _signalsDisposed;
  private int _state = (int)LogPipelineState.Starting;
  private int _pendingSinceFlush;
  private long _enqueued;
  private long _droppedNormal;
  private long _droppedHigh;
  private ConsumptionSnapshot _consumption = new(0, 0, 0, 0, 0, false, false);
  private long _droppedByBackpressure;
  private int _consumerFailed;
  private int _stopFailed;
  private System.Timers.Timer? _healthTimer;

  public LogPipeline(Func<IReadOnlyList<LogEvent>, LogBatchWriteResult> writeBatch, LogPipelineOptions? options = null)
  {
    _writeBatch = writeBatch ?? throw new ArgumentNullException(nameof(writeBatch));
    _options = options ?? new LogPipelineOptions();
    _normalQueue = Channel.CreateBounded<LogEvent>(CreateBoundedOptions(_options.NormalQueueCapacity));
    _highQueue = Channel.CreateBounded<LogEvent>(CreateBoundedOptions(_options.HighQueueCapacity));
  }

  public LogPipelineState State => (LogPipelineState)Volatile.Read(ref _state);

  public int NormalQueueDepth => _normalQueue.Reader.Count;
  public int HighQueueDepth => _highQueue.Reader.Count;
  internal int PendingFlushGenerations
  {
    get { lock (_gate) return (_activeFlush is null ? 0 : 1) + (_nextFlush is null ? 0 : 1); }
  }

  public LogPipelineDiagnostics Diagnostics
  {
    get
    {
      // One immutable consumer partition; producer counters are independent
      // monotonic observations, not a global cross-thread transaction.
      var sample = Volatile.Read(ref _consumption);
      return new(Interlocked.Read(ref _enqueued), Interlocked.Read(ref _droppedNormal),
        Interlocked.Read(ref _droppedHigh), sample.Primary + sample.Fallback, sample.FailedBatches,
        sample.Primary, sample.Fallback, sample.Lost, sample.PrimaryFailures,
        Interlocked.Read(ref _droppedByBackpressure),
        Volatile.Read(ref _consumerFailed) != 0, Volatile.Read(ref _stopFailed) != 0);
    }
  }

  public void RecordBackpressureDrop()
  {
    Interlocked.Increment(ref _droppedByBackpressure);
    CountDrop(LogPriority.Normal);
  }

  public void Start()
  {
    lock (_gate)
    {
      if (State != LogPipelineState.Starting) return;
      if (_coldDraining) throw new InvalidOperationException("A cold flush owns the reader.");
      Volatile.Write(ref _state, (int)LogPipelineState.Running);
      _consumerTask = Task.Run(ConsumeLoopAsync);
    }
  }

  public bool TryEnqueue(LogPriority priority, LogEvent logEvent)
  {
    lock (_gate)
    {
      if (State is LogPipelineState.Flushing or LogPipelineState.Stopped)
      {
        CountDrop(priority);
        return false;
      }
      var queue = priority == LogPriority.High ? _highQueue : _normalQueue;
      if (queue.Writer.TryWrite(logEvent))
      {
        if (priority == LogPriority.High) _acceptedHigh++;
        else _acceptedNormal++;
        Interlocked.Increment(ref _enqueued);
        if (++_pendingSinceFlush >= _options.MaxBatchSize) SignalConsumer();
        return true;
      }
      CountDrop(priority);
      return false;
    }
  }

  public async Task<bool> FlushAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    Task<bool>? confirmation = null;
    lock (_gate)
    {
      // Async flush never executes a cold writer on its caller. Starting the
      // existing single consumer also coalesces concurrent cold requests.
      if (_consumerTask is null && !_coldDraining && !_stopRequested)
      {
        Volatile.Write(ref _state, (int)LogPipelineState.Running);
        _consumerTask = Task.Run(ConsumeLoopAsync);
      }
      if (_consumerTask is not null)
      {
        if (_stopRequested) confirmation = _consumerTask;
        else if (_consumerTask.IsCompleted) return false;
        else if (_processedNormal >= _acceptedNormal && _processedHigh >= _acceptedHigh)
          return Volatile.Read(ref _consumerFailed) == 0;
        else
        {
          if (_activeFlush is null)
            _activeFlush = NewGeneration();
          else if (_activeFlush.Normal < _acceptedNormal || _activeFlush.High < _acceptedHigh)
          {
            _nextFlush ??= NewGeneration();
            // Next is coalesced while pending, then frozen when promoted.
            _nextFlush.Normal = _acceptedNormal;
            _nextFlush.High = _acceptedHigh;
          }
          confirmation = (_nextFlush ?? _activeFlush).Completion.Task;
          SignalConsumer();
        }
      }
    }
    if (confirmation is null) return false; // A synchronous cold reader owns it.
    try
    {
      return await confirmation.WaitAsync(TimeSpan.FromMilliseconds(_options.ShutdownTimeoutMs), cancellationToken)
        .ConfigureAwait(false);
    }
    catch (TimeoutException) { return false; }
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
    Task<bool> consumer;
    lock (_gate)
    {
      if (!_stopRequested)
      {
        _stopRequested = true;
        _stopNormal = _acceptedNormal;
        _stopHigh = _acceptedHigh;
        _stopLostBaseline = Volatile.Read(ref _consumption).Lost;
        Volatile.Write(ref _state, (int)LogPipelineState.Flushing);
        _normalQueue.Writer.TryComplete();
        _highQueue.Writer.TryComplete();
      }
      // A synchronous test reader can finish late, just like a blocked writer.
      // Transfer ownership through one shared completion, without polling or
      // creating a second reader while its callback is still executing.
      var coldRelease = _coldReleased?.Task ?? Task.CompletedTask;
      consumer = _consumerTask ??= Task.Run(async () =>
      {
        await coldRelease.ConfigureAwait(false);
        return await ConsumeLoopAsync().ConfigureAwait(false);
      });
      SignalConsumer();
    }
    try
    {
      var confirmed = await consumer.WaitAsync(TimeSpan.FromMilliseconds(_options.ShutdownTimeoutMs)).ConfigureAwait(false);
      lock (_gate)
      {
        var success = confirmed && !_stopTimedOut;
        if (!success) Volatile.Write(ref _stopFailed, 1);
        return success;
      }
    }
    catch (TimeoutException)
    {
      lock (_gate)
      {
        _stopTimedOut = true;
        Volatile.Write(ref _stopFailed, 1);
        Volatile.Write(ref _state, (int)LogPipelineState.Stopped);
      }
      // A late callback retains ownership of the signal until its task exits.
      return false;
    }
    catch
    {
      Volatile.Write(ref _consumerFailed, 1);
      Volatile.Write(ref _stopFailed, 1);
      return false;
    }
  }

  public bool FlushOnce()
  {
    long normal, high;
    lock (_gate)
    {
      if (_coldDraining || _consumerTask is { IsCompleted: false }) return false;
      _coldDraining = true;
      _coldReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
      normal = _acceptedNormal;
      high = _acceptedHigh;
    }
    try
    {
      var allOk = true;
      while (_processedNormal < normal || _processedHigh < high)
      {
        var batch = TryFillBatch(normal, high, out var normals, out var highs);
        if (batch.Count == 0) return false;
        allOk &= WriteBatch(batch, normals, highs);
      }
      return allOk;
    }
    finally
    {
      lock (_gate)
      {
        _coldDraining = false;
        _coldReleased!.TrySetResult();
        _coldReleased = null;
      }
    }
  }

  private bool WriteBatch(IReadOnlyList<LogEvent> batch, int normals, int highs)
  {
    LogBatchWriteResult result;
    try
    {
      result = _writeBatch(batch);
      if (!result.IsValidFor(batch.Count))
        throw new InvalidOperationException("Invalid log batch partition.");
    }
    catch
    {
      Volatile.Write(ref _consumerFailed, 1);
      result = LogBatchWriteResult.Failed(batch.Count);
    }
    lock (_gate)
    {
      // Partition and processed watermarks commit together. Otherwise Flush
      // can mistake a still-pending batch's loss for pre-request history.
      var old = Volatile.Read(ref _consumption);
      Volatile.Write(ref _consumption, new(old.Primary + result.PrimaryWritten,
        old.Fallback + result.FallbackWritten, old.Lost + result.Lost,
        old.FailedBatches + (result.Lost > 0 ? 1 : 0),
        old.PrimaryFailures + (result.PrimaryAttemptFailed ? 1 : 0),
        result.FallbackWritten > 0, result.Lost > 0));
      _processedNormal += normals;
      _processedHigh += highs;
    }
    return result.FullyPersisted;
  }

  private async Task<bool> ConsumeLoopAsync()
  {
    try
    {
      while (true)
      {
        await _wakeSignal.WaitAsync(TimeSpan.FromMilliseconds(_options.FlushIntervalMs)).ConfigureAwait(false);
        while (true)
        {
          long normal, high;
          lock (_gate)
          {
            CompleteReadyGeneration();
            if (_stopRequested && _processedNormal >= _stopNormal && _processedHigh >= _stopHigh)
              return Volatile.Read(ref _consumerFailed) == 0
                && Volatile.Read(ref _consumption).Lost == _stopLostBaseline && !_stopTimedOut;
            normal = _activeFlush?.Normal ?? (_stopRequested ? _stopNormal : _acceptedNormal);
            high = _activeFlush?.High ?? (_stopRequested ? _stopHigh : _acceptedHigh);
            _pendingSinceFlush = 0;
          }
          var batch = TryFillBatch(normal, high, out var normals, out var highs);
          if (batch.Count == 0) break;
          WriteBatch(batch, normals, highs);
        }
      }
    }
    catch
    {
      Volatile.Write(ref _consumerFailed, 1);
      return false;
    }
    finally
    {
      lock (_gate)
      {
        Volatile.Write(ref _state, (int)LogPipelineState.Stopped);
        _activeFlush?.Completion.TrySetResult(false);
        _nextFlush?.Completion.TrySetResult(false);
        _activeFlush = _nextFlush = null;
        _signalsDisposed = true;
        _wakeSignal.Dispose();
      }
    }
  }

  private FlushGeneration NewGeneration() => new(_acceptedNormal, _acceptedHigh, Volatile.Read(ref _consumption).Lost);

  private void CompleteReadyGeneration()
  {
    while (_activeFlush is { } generation
      && _processedNormal >= generation.Normal && _processedHigh >= generation.High)
    {
      generation.Completion.TrySetResult(Volatile.Read(ref _consumerFailed) == 0
        && Volatile.Read(ref _consumption).Lost == generation.LostBaseline);
      _activeFlush = _nextFlush;
      _nextFlush = null;
    }
  }

  private void SignalConsumer()
  {
    if (_signalsDisposed) return;
    try { _wakeSignal.Release(); }
    catch (SemaphoreFullException) { /* Wake already pending; signal is coalesced. */ }
  }

  private void CountDrop(LogPriority priority)
  {
    if (priority == LogPriority.High) Interlocked.Increment(ref _droppedHigh);
    else Interlocked.Increment(ref _droppedNormal);
  }

  private List<LogEvent> TryFillBatch(long normalWatermark, long highWatermark, out int normals, out int highs)
  {
    normals = highs = 0;
    var batch = new List<LogEvent>(_options.MaxBatchSize);
    while (batch.Count < _options.MaxBatchSize && _processedHigh + highs < highWatermark && _highQueue.Reader.TryRead(out var high))
    {
      batch.Add(high);
      highs++;
    }
    while (batch.Count < _options.MaxBatchSize && _processedNormal + normals < normalWatermark && _normalQueue.Reader.TryRead(out var normal))
    {
      batch.Add(normal);
      normals++;
    }
    return batch;
  }

  private static BoundedChannelOptions CreateBoundedOptions(int capacity) => new(capacity)
  {
    FullMode = BoundedChannelFullMode.Wait,
    SingleReader = true,
    SingleWriter = false,
  };

  private sealed record ConsumptionSnapshot(long Primary, long Fallback, long Lost,
    long FailedBatches, long PrimaryFailures, bool LastFallback, bool LastLost);

  private sealed class FlushGeneration(long normal, long high, long lostBaseline)
  {
    public long Normal = normal;
    public long High = high;
    public readonly long LostBaseline = lostBaseline;
    public readonly TaskCompletionSource<bool> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
  }
}
