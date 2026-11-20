namespace SmartGuard.Configuration;

public sealed record LogPipelineDiagnostics(
  long Enqueued,
  long DroppedNormal,
  long DroppedHigh,
  long WrittenLines,
  long FailedBatches,
  long PrimaryWrittenLines = 0,
  long FallbackWrittenLines = 0,
  long LostLines = 0,
  long PrimaryFailedBatches = 0,
  long DroppedByBackpressure = 0,
  bool ConsumerFailed = false,
  bool StopFailed = false);
