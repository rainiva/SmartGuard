namespace SmartGuard.Configuration;

// Migration shell: forwards to LogEvent until P0-03 removes this legacy DTO.
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
