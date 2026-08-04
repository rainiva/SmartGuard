using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal sealed record LogSessionMainViewState(
  LogQueryExpression BaseExpression,
  string SearchKeyword,
  IReadOnlyList<string> ActiveTags,
  LogViewTimeRange TimeRange,
  DateTime? CustomRangeStart,
  DateTime? CustomRangeEnd,
  bool SearchCaseSensitive,
  LogViewportAnchor? Anchor,
  string? SelectedEventId);

internal sealed class LogSessionCoordinator
{
  private LogSessionMainViewState? _savedMainView;
  private LogSessionMetadata? _activeSession;

  internal bool IsActive => _activeSession is not null;

  internal LogSessionMetadata? ActiveSession => _activeSession;

  internal string? ActiveTitle =>
    _activeSession is null ? null : LogSessionProjector.BuildSessionTitle(_activeSession);

  internal void SelectSession(LogSessionMetadata session, LogSessionMainViewState mainViewState)
  {
    _savedMainView = mainViewState;
    _activeSession = session;
  }

  internal LogQueryExpression BuildActiveQuery()
  {
    if (_activeSession is null)
      throw new InvalidOperationException("Session view is not active.");

    var sessionClause = LogSessionProjector.BuildSessionExpression(_activeSession);
    return LogSessionProjector.CombineWithBase(_savedMainView?.BaseExpression, sessionClause);
  }

  internal LogSessionMainViewState? ExitSessionView()
  {
    var saved = _savedMainView;
    _savedMainView = null;
    _activeSession = null;
    return saved;
  }

  internal async Task<IReadOnlyList<LogSessionMetadata>> LoadSessionMetadataAsync(
    ILogEventSource eventSource,
    CancellationToken cancellationToken)
  {
    var service = new LogQueryService();
    return await service.ProjectSessionsAsync(eventSource, cancellationToken);
  }
}
