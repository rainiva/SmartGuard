namespace SmartGuard.Configuration;

/// <summary>
/// 日志行索引条目：从 <see cref="LogEvent"/> 提取的可查询字段与稳定物理定位。
/// </summary>
public sealed record LogIndexEntry(
  LogSourceIdentity Source,
  long ByteOffset,
  string EventId,
  DateTimeOffset Timestamp,
  string Level,
  string Category,
  string? EventName,
  string? OperationId,
  string? TraceId,
  int? ProcessId,
  int? ThreadId);
