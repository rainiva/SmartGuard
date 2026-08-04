namespace SmartGuard.Configuration;

public sealed record LogSessionMetadata(
  string SessionId,
  DateTimeOffset StartTimestamp,
  DateTimeOffset? EndTimestamp,
  int? ProcessId,
  string Component,
  bool IsActive,
  string StartEventId,
  string? EndEventId);
