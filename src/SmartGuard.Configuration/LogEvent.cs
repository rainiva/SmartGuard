using System.Text.Json;

namespace SmartGuard.Configuration;

public sealed record LogEvent(
  int SchemaVersion,
  string EventId,
  DateTimeOffset Timestamp,
  LogSeverity Severity,
  string Component,
  string Channel,
  string? EventName,
  string Message,
  LogExceptionInfo? Exception,
  string? OperationId,
  string? TraceId,
  string? SpanId,
  string? UserActionId,
  int? ProcessId,
  int? ThreadId,
  IReadOnlyDictionary<string, JsonElement> Properties)
{
  public const int CurrentSchemaVersion = 1;

  public static IReadOnlyDictionary<string, JsonElement> EmptyProperties { get; } =
    new Dictionary<string, JsonElement>();
}
