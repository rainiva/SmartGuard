namespace SmartGuard.Configuration;

public sealed record LogJsonlEntry(
  DateTimeOffset Timestamp,
  string Level,
  string Category,
  string? EventName,
  string Message,
  string? OperationId,
  string? TraceId,
  string? SpanId,
  string? UserActionId,
  int? ProcessId,
  int? ThreadId);
