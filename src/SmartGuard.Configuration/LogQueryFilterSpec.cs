namespace SmartGuard.Configuration;

/// <summary>
/// 结构化筛选规格 record：所有常用筛选字段集中管理。
/// 参考标准文档 §23 UI 查询：时间/等级/模块/事件/OperationId/关键词/PID/TID。
/// </summary>
public sealed record LogQueryFilterSpec(
  string? Keyword,
  string? Level,
  string? Category,
  string? OperationId,
  string? TraceId,
  int? ProcessId,
  int? ThreadId,
  string? EventName,
  DateTimeOffset? StartTime,
  DateTimeOffset? EndTime,
  int Page,
  int PageSize)
{
  public static LogQueryFilterSpec Default =>
    new(null, null, null, null, null, null, null, null, null, null, 1, 50);
}
