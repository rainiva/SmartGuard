namespace SmartGuard.Configuration;

/// <summary>
/// 日志查询请求：多条件筛选 + 分页参数。
/// 所有筛选字段为可选（null 表示不限制）。
/// </summary>
public sealed record LogQueryRequest(
  string? Keyword,
  string? Level,
  string? Category,
  string? OperationId,
  string? TraceId,
  int? ProcessId,
  int? ThreadId,
  string? EventName,
  int Page,
  int PageSize);
