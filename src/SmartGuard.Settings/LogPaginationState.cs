namespace SmartGuard.Settings;

internal sealed record LogPaginationState
{
  private static readonly HashSet<int> AllowedPageSizes = [20, 50, 100];

  private readonly int _lastSuccessfulPage;
  private readonly int _lastSuccessfulTotalPages;
  private readonly int _lastSuccessfulPageSize;
  private readonly int? _lastSuccessfulExactTotalCount;
  private readonly bool _lastSuccessfulIsScanComplete;
  private readonly int? _requestedPage;

  private LogPaginationState(
    int currentPage,
    int pageSize,
    int? exactTotalCount,
    int totalPages,
    bool isBusy,
    bool isStale,
    bool isScanComplete,
    int lastSuccessfulPage,
    int lastSuccessfulTotalPages,
    int lastSuccessfulPageSize,
    int? lastSuccessfulExactTotalCount,
    bool lastSuccessfulIsScanComplete,
    int? requestedPage)
  {
    CurrentPage = currentPage;
    PageSize = pageSize;
    ExactTotalCount = exactTotalCount;
    TotalPages = totalPages;
    IsBusy = isBusy;
    IsStale = isStale;
    IsScanComplete = isScanComplete;
    _lastSuccessfulPage = lastSuccessfulPage;
    _lastSuccessfulTotalPages = lastSuccessfulTotalPages;
    _lastSuccessfulPageSize = lastSuccessfulPageSize;
    _lastSuccessfulExactTotalCount = lastSuccessfulExactTotalCount;
    _lastSuccessfulIsScanComplete = lastSuccessfulIsScanComplete;
    _requestedPage = requestedPage;
  }

  internal static LogPaginationState Initial { get; } = new(
    currentPage: 1,
    pageSize: 20,
    exactTotalCount: null,
    totalPages: 1,
    isBusy: false,
    isStale: false,
    isScanComplete: false,
    lastSuccessfulPage: 1,
    lastSuccessfulTotalPages: 1,
    lastSuccessfulPageSize: 20,
    lastSuccessfulExactTotalCount: null,
    lastSuccessfulIsScanComplete: false,
    requestedPage: null);

  internal int CurrentPage { get; }

  internal int PageSize { get; }

  internal int? ExactTotalCount { get; }

  internal int TotalPages { get; }

  internal bool IsBusy { get; }

  internal bool IsStale { get; }

  internal bool IsScanComplete { get; }

  internal bool CanGoFirst => CanNavigateBoundary && CurrentPage > 1;

  internal bool CanGoPrevious => CanNavigateBoundary && CurrentPage > 1;

  internal bool CanGoNext => CanNavigateBoundary && CurrentPage < TotalPages;

  internal bool CanGoLast => CanNavigateBoundary && CurrentPage < TotalPages;

  internal bool CanNavigateToPage(int pageNumber)
    => CanNavigateBoundary
       && pageNumber >= 1
       && pageNumber <= TotalPages
       && pageNumber != CurrentPage;

  internal LogPaginationState ApplyExactCount(int exactTotalCount, bool isScanComplete)
  {
    var normalizedCount = Math.Max(0, exactTotalCount);
    var totalPages = CalculateTotalPages(normalizedCount, PageSize);
    var currentPage = Math.Clamp(_requestedPage ?? CurrentPage, 1, totalPages);
    return new LogPaginationState(
      currentPage,
      PageSize,
      normalizedCount,
      totalPages,
      isBusy: false,
      isStale: false,
      isScanComplete,
      lastSuccessfulPage: currentPage,
      lastSuccessfulTotalPages: totalPages,
      lastSuccessfulPageSize: PageSize,
      lastSuccessfulExactTotalCount: normalizedCount,
      lastSuccessfulIsScanComplete: isScanComplete,
      requestedPage: null);
  }

  internal LogPaginationState ChangePageSize(int pageSize)
  {
    if (!AllowedPageSizes.Contains(pageSize))
      throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Page size must be 20, 50, or 100.");

    var totalPages = ExactTotalCount.HasValue
      ? CalculateTotalPages(ExactTotalCount.Value, pageSize)
      : 1;
    return new LogPaginationState(
      currentPage: 1,
      pageSize,
      exactTotalCount: ExactTotalCount,
      totalPages,
      isBusy: false,
      isStale: IsStale,
      isScanComplete: IsScanComplete,
      lastSuccessfulPage: _lastSuccessfulPage,
      lastSuccessfulTotalPages: _lastSuccessfulTotalPages,
      lastSuccessfulPageSize: _lastSuccessfulPageSize,
      lastSuccessfulExactTotalCount: _lastSuccessfulExactTotalCount,
      lastSuccessfulIsScanComplete: _lastSuccessfulIsScanComplete,
      requestedPage: null);
  }

  internal LogPaginationState ResetForFilterChange() => ResetToFirstPage();

  internal LogPaginationState ResetForSortChange() => ResetToFirstPage();

  internal LogPaginationState ResetForSessionChange() => ResetToFirstPage();

  internal LogPaginationState ResetForExplicitRefresh() => ResetToFirstPage();

  internal LogPaginationState StartNavigation(int pageNumber)
  {
    var targetPage = Math.Clamp(pageNumber, 1, TotalPages);
    return new LogPaginationState(
      targetPage,
      PageSize,
      ExactTotalCount,
      TotalPages,
      isBusy: true,
      isStale: IsStale,
      isScanComplete: IsScanComplete,
      lastSuccessfulPage: _lastSuccessfulPage,
      lastSuccessfulTotalPages: _lastSuccessfulTotalPages,
      lastSuccessfulPageSize: _lastSuccessfulPageSize,
      lastSuccessfulExactTotalCount: _lastSuccessfulExactTotalCount,
      lastSuccessfulIsScanComplete: _lastSuccessfulIsScanComplete,
      requestedPage: targetPage);
  }

  internal LogPaginationState MarkCountInexact(bool isScanComplete)
    => new(
      CurrentPage,
      PageSize,
      exactTotalCount: null,
      totalPages: Math.Max(1, CurrentPage),
      isBusy: false,
      isStale: IsStale,
      isScanComplete,
      lastSuccessfulPage: _lastSuccessfulPage,
      lastSuccessfulTotalPages: _lastSuccessfulTotalPages,
      lastSuccessfulPageSize: _lastSuccessfulPageSize,
      lastSuccessfulExactTotalCount: _lastSuccessfulExactTotalCount,
      lastSuccessfulIsScanComplete: _lastSuccessfulIsScanComplete,
      requestedPage: null);

  internal LogPaginationState FailGeneration()
    => new(
      currentPage: _lastSuccessfulPage,
      pageSize: _lastSuccessfulPageSize,
      exactTotalCount: _lastSuccessfulExactTotalCount,
      totalPages: _lastSuccessfulTotalPages,
      isBusy: false,
      isStale: true,
      isScanComplete: _lastSuccessfulIsScanComplete,
      lastSuccessfulPage: _lastSuccessfulPage,
      lastSuccessfulTotalPages: _lastSuccessfulTotalPages,
      lastSuccessfulPageSize: _lastSuccessfulPageSize,
      lastSuccessfulExactTotalCount: _lastSuccessfulExactTotalCount,
      lastSuccessfulIsScanComplete: _lastSuccessfulIsScanComplete,
      requestedPage: null);

  private bool CanNavigateBoundary => !IsBusy && ExactTotalCount.HasValue && TotalPages > 1;

  private LogPaginationState ResetToFirstPage()
    => new(
      currentPage: 1,
      PageSize,
      exactTotalCount: null,
      totalPages: 1,
      isBusy: false,
      isStale: IsStale,
      isScanComplete: false,
      lastSuccessfulPage: _lastSuccessfulPage,
      lastSuccessfulTotalPages: _lastSuccessfulTotalPages,
      lastSuccessfulPageSize: _lastSuccessfulPageSize,
      lastSuccessfulExactTotalCount: _lastSuccessfulExactTotalCount,
      lastSuccessfulIsScanComplete: _lastSuccessfulIsScanComplete,
      requestedPage: null);

  private static int CalculateTotalPages(int exactTotalCount, int pageSize)
    => Math.Max(1, (int)Math.Ceiling(exactTotalCount / (double)pageSize));
}
