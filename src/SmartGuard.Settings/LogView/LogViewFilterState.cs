namespace SmartGuard.Settings.LogView;

internal sealed record LogViewFilterState(
  string SearchKeyword,
  bool SearchCaseSensitive,
  IReadOnlyList<string> ActiveTagFilters,
  LogViewTimeRange TimeRange,
  DateTime? CustomRangeStart,
  DateTime? CustomRangeEnd,
  string OperationIdFilter,
  string EventNameFilter,
  int? ProcessIdFilter,
  int? ThreadIdFilter,
  Func<DateTime>? NowProvider)
{
  internal bool HasActiveFilters =>
    !string.IsNullOrWhiteSpace(SearchKeyword)
    || ActiveTagFilters.Count > 0
    || TimeRange != LogViewTimeRange.All
    || !string.IsNullOrWhiteSpace(OperationIdFilter)
    || !string.IsNullOrWhiteSpace(EventNameFilter)
    || ProcessIdFilter is not null
    || ThreadIdFilter is not null;
}
