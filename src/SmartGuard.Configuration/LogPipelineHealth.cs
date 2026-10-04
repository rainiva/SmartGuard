using System.Diagnostics;
using System.Text.Json;

namespace SmartGuard.Configuration;

/// <summary>
/// 累计健康统计；未测量的延迟为 null，不把缺少样本解释为零延迟。
/// 既有健康采样定时器通过正常管线输出结构化事件。
/// </summary>
public sealed record LogHealthSnapshot(
  long EnqueuedTotal,
  long DroppedNormal,
  long DroppedHigh,
  long WrittenLinesTotal,
  long FailedBatches,
  int NormalQueueDepth,
  int HighQueueDepth,
  double EnqueueRatePerSec,
  double WriteRatePerSec,
  double? AverageLatencyMs,
  double? P99LatencyMs,
  long PrimaryWrittenLines = 0,
  long FallbackWrittenLines = 0,
  long LostLines = 0,
  long PrimaryFailedBatches = 0,
  long DroppedByBackpressure = 0,
  bool ConsumerFailed = false,
  bool StopFailed = false);

public sealed class LogPipelineHealth
{
  private long _previousEnqueued;
  private long _previousWritten;
  private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
  private double _previousElapsedSec;

  public LogHealthSnapshot TakeSnapshot(LogPipeline pipeline)
  {
    var diag = pipeline.Diagnostics;
    var currentSec = _stopwatch.Elapsed.TotalSeconds;
    var deltaSec = Math.Max(0.001, currentSec - _previousElapsedSec);

    var enqueueRate = (diag.Enqueued - _previousEnqueued) / deltaSec;
    var writeRate = (diag.WrittenLines - _previousWritten) / deltaSec;

    _previousEnqueued = diag.Enqueued;
    _previousWritten = diag.WrittenLines;
    _previousElapsedSec = currentSec;

    return new LogHealthSnapshot(
      EnqueuedTotal: diag.Enqueued,
      DroppedNormal: diag.DroppedNormal,
      DroppedHigh: diag.DroppedHigh,
      WrittenLinesTotal: diag.WrittenLines,
      FailedBatches: diag.FailedBatches,
      NormalQueueDepth: pipeline.NormalQueueDepth,
      HighQueueDepth: pipeline.HighQueueDepth,
      EnqueueRatePerSec: Math.Round(enqueueRate, 2),
      WriteRatePerSec: Math.Round(writeRate, 2),
      AverageLatencyMs: null,
      P99LatencyMs: null,
      PrimaryWrittenLines: diag.PrimaryWrittenLines,
      FallbackWrittenLines: diag.FallbackWrittenLines,
      LostLines: diag.LostLines,
      PrimaryFailedBatches: diag.PrimaryFailedBatches,
      DroppedByBackpressure: diag.DroppedByBackpressure,
      ConsumerFailed: diag.ConsumerFailed,
      StopFailed: diag.StopFailed);
  }

  public LogEvent CreateHealthEvent(LogHealthSnapshot snapshot)
    => LogStructuredSink.CreatePipelineHealthEvent(snapshot);

  public string FormatJson(LogHealthSnapshot snapshot)
  {
    return JsonSerializer.Serialize(new
    {
      health = "LogPipeline",
      enqueuedTotal = snapshot.EnqueuedTotal,
      droppedNormal = snapshot.DroppedNormal,
      droppedHigh = snapshot.DroppedHigh,
      writtenLinesTotal = snapshot.WrittenLinesTotal,
      failedBatches = snapshot.FailedBatches,
      primaryWrittenLines = snapshot.PrimaryWrittenLines,
      fallbackWrittenLines = snapshot.FallbackWrittenLines,
      lostLines = snapshot.LostLines,
      primaryFailedBatches = snapshot.PrimaryFailedBatches,
      droppedByBackpressure = snapshot.DroppedByBackpressure,
      consumerFailed = snapshot.ConsumerFailed,
      stopFailed = snapshot.StopFailed,
      normalQueueDepth = snapshot.NormalQueueDepth,
      highQueueDepth = snapshot.HighQueueDepth,
      enqueueRatePerSec = snapshot.EnqueueRatePerSec,
      writeRatePerSec = snapshot.WriteRatePerSec,
      averageLatencyMs = snapshot.AverageLatencyMs,
      p99LatencyMs = snapshot.P99LatencyMs
    });
  }

  public string FormatLogLine(LogHealthSnapshot snapshot, DateTime timestamp)
    => $"[DIAG] {timestamp:yyyy-MM-dd HH:mm:ss} {FormatJson(snapshot)}";
}
