namespace SmartGuard.Configuration;

public sealed record LogQueryPageRequest(
  int PageNumber,
  int PageSize,
  LogSortDirection SortDirection);
