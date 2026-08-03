namespace SmartGuard.Configuration;

/// <summary>
/// 日志查询结果：匹配条目 + 总数 + 分页信息。
/// </summary>
public sealed record LogQueryResult(
  IReadOnlyList<LogIndexEntry> Matches,
  int TotalCount,
  int Page,
  int PageSize,
  int MatchCount = 0,
  int? ExactTotalCount = null,
  bool IsScanComplete = true,
  LogQueryCursor? NextCursor = null,
  IReadOnlyList<LogMatch>? OrderedMatches = null,
  string? ErrorMessage = null,
  bool AbortedDueToRegexTimeout = false,
  int ScannedCount = 0);
