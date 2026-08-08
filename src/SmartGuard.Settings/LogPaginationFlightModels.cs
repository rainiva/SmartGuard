using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal sealed record LogPaginationIntent(
  int QueryGeneration,
  LogQueryExpression Expression,
  LogReadScope Scope,
  int PageNumber,
  int PageSize,
  LogSortDirection SortDirection,
  bool IgnoreCase,
  bool CaptureNewSnapshot);

internal sealed record LogPaginationApplyModel(
  int QueryGeneration,
  bool IsProvisional,
  IReadOnlyList<LogEvent> Events,
  int PageNumber,
  int PageSize,
  int? ExactTotalCount,
  bool IsScanComplete,
  LogSortDirection SortDirection,
  LogSourceSnapshot Snapshot,
  LogQueryPageIndex? Index,
  string? ErrorMessage);
