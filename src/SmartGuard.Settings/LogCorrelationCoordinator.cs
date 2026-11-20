using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal sealed record LogCorrelationMainViewState(
  LogQueryExpression BaseExpression,
  string SearchKeyword,
  IReadOnlyList<string> ActiveTags,
  LogViewTimeRange TimeRange,
  DateTime? CustomRangeStart,
  DateTime? CustomRangeEnd,
  bool SearchCaseSensitive,
  LogViewportAnchor? Anchor,
  string? SelectedEventId)
{
  internal int PageNumber { get; init; } = 1;

  internal LogSortDirection SortDirection { get; init; } = LogSortDirection.NewestFirst;

  internal string LevelFilter { get; init; } = string.Empty;

  internal string ComponentFilter { get; init; } = string.Empty;
}

internal sealed class LogCorrelationCoordinator
{
  private LogCorrelationMainViewState? _savedMainView;
  private LogOperationCorrelationKey? _activeKey;

  internal bool IsActive => _activeKey is not null;

  internal string? ActiveTitle =>
    _activeKey is null ? null : LogOperationProjector.BuildContextTitle(_activeKey);

  internal LogOperationCorrelationKey? ActiveKey => _activeKey;

  internal bool TryEnterCorrelationView(LogEvent anchorEvent, LogCorrelationMainViewState mainViewState)
  {
    var key = LogOperationGraph.ResolveCorrelationKey(anchorEvent);
    if (key is null)
      return false;

    _savedMainView = mainViewState;
    _activeKey = key;
    return true;
  }

  internal LogQueryExpression BuildActiveQuery()
  {
    if (_activeKey is null)
      throw new InvalidOperationException("Correlation view is not active.");

    var baseExpression = _savedMainView?.BaseExpression;
    var activeTags = string.IsNullOrWhiteSpace(_savedMainView?.LevelFilter)
      ? _savedMainView?.ActiveTags ?? []
      : [_savedMainView.LevelFilter];
    foreach (var tag in activeTags)
    {
      baseExpression = LogOperationGraph.CombineWithBase(
        baseExpression,
        LogTagFilterCatalog.BuildQueryExpression(tag));
    }

    var correlationClause = LogOperationGraph.BuildCorrelationExpression(_activeKey);
    return LogOperationGraph.CombineWithBase(baseExpression, correlationClause);
  }

  internal LogCorrelationMainViewState? ExitCorrelationView()
  {
    var saved = _savedMainView;
    _savedMainView = null;
    _activeKey = null;
    return saved;
  }
}
