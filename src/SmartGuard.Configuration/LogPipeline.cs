using System.Threading.Channels;

namespace SmartGuard.Configuration;

public sealed class LogPipeline
{
  private readonly Channel<string> _normalQueue;
  private readonly Channel<string> _highQueue;
  private readonly Func<IReadOnlyList<string>, bool> _writeBatch;
  private readonly LogPipelineOptions _options;
  private long _enqueued;
  private long _droppedNormal;
  private long _droppedHigh;
  private long _writtenLines;
  private long _failedBatches;

  public LogPipeline(Func<IReadOnlyList<string>, bool> writeBatch, LogPipelineOptions? options = null)
  {
    _writeBatch = writeBatch ?? throw new ArgumentNullException(nameof(writeBatch));
    _options = options ?? new LogPipelineOptions();
    _normalQueue = Channel.CreateBounded<string>(CreateBoundedOptions(_options.NormalQueueCapacity));
    _highQueue = Channel.CreateBounded<string>(CreateBoundedOptions(_options.HighQueueCapacity));
  }

  public LogPipelineDiagnostics Diagnostics => new(
    Interlocked.Read(ref _enqueued),
    Interlocked.Read(ref _droppedNormal),
    Interlocked.Read(ref _droppedHigh),
    Interlocked.Read(ref _writtenLines),
    Interlocked.Read(ref _failedBatches));

  public bool TryEnqueue(LogPriority priority, string line)
  {
    var queue = priority == LogPriority.High ? _highQueue : _normalQueue;
    if (queue.Writer.TryWrite(line))
    {
      Interlocked.Increment(ref _enqueued);
      return true;
    }

    // FullMode.Wait + TryWrite：队列满时 TryWrite 返回 false，可精确计数丢弃；
    // 禁用 DropWrite（其 TryWrite 恒 true，静默丢行且无法计数）。
    if (priority == LogPriority.High) Interlocked.Increment(ref _droppedHigh);
    else Interlocked.Increment(ref _droppedNormal);
    return false;
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
    SingleReader = true,  // Slice 3 起唯一后台消费者
    SingleWriter = false, // 业务线程多写者
  };
}
