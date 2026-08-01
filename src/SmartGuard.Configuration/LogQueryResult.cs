namespace SmartGuard.Configuration;

/// <summary>
/// 日志查询结果：匹配条目 + 总数 + 分页信息。
/// </summary>
public sealed record LogQueryResult(
  IReadOnlyList<LogIndexEntry> Matches,
  int TotalCount,
  int Page,
  int PageSize);
