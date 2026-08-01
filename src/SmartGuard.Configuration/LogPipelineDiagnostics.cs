namespace SmartGuard.Configuration;

public sealed record LogPipelineDiagnostics(
  long Enqueued,
  long DroppedNormal,
  long DroppedHigh,
  long WrittenLines,
  long FailedBatches);
