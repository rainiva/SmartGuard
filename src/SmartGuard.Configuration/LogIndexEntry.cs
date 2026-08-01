namespace SmartGuard.Configuration;

/// <summary>
/// 日志行索引条目：从日志文件单行解析出的结构化字段。
/// 用于 LogQueryService 快速筛选，避免全文件扫描。
/// </summary>
public sealed record LogIndexEntry(
  int LineNumber,
  DateTimeOffset Timestamp,
  string Level,
  string Category,
  string? EventName,
  string? OperationId,
  string? TraceId,
  int? ProcessId,
  int? ThreadId);
