using System.Diagnostics;
using System.Text.Json;

namespace SmartGuard.Configuration;

/// <summary>
/// 可观测健康指标快照：8 项内部指标 + 入队/落盘速率。
/// 定时（默认 5 分钟）以带标准时间戳的 [DIAG] JSON 行追加到主日志末尾（D4 裁决）。
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
  double AverageLatencyMs,
  double P99LatencyMs);

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
      AverageLatencyMs: 0, // 延迟跟踪需管线内部插桩，阶段三简化为零
      P99LatencyMs: 0);    // P99 同上，留接口供后续精确实现
  }

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
