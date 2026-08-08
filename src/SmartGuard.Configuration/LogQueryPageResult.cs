namespace SmartGuard.Configuration;

public sealed record LogQueryPageResult(
  IReadOnlyList<LogEvent> Events,
  int PageNumber,
  int PageSize,
  int ExactTotalCount,
  int TotalPages,
  LogSortDirection SortDirection,
  LogSourceSnapshot Snapshot,
  bool HasPreviousPage,
  bool HasNextPage);
