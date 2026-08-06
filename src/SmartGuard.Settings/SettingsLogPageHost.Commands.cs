using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal sealed partial class SettingsLogPageHost
{
  private LogViewRefreshKind? _pendingHistoryBoundary;

  private void WireLogWorkbenchCommands()
  {
    SettingsLogPageHostWiring.WireKeyboardCommands(
      _window,
      () => _logPageActive && !_disposed,
      SettingsLogPageHostWiring.IsTextInputFocused,
      () => _logSearchFilterBar?.FocusKeywordInput(),
      () => _searchCoordinator.NavigateMatch(
        LogMatchNavigationDirection.Forward,
        _logListPresenter,
        _logViewport,
        loadPage: null),
      () => _searchCoordinator.NavigateMatch(
        LogMatchNavigationDirection.Backward,
        _logListPresenter,
        _logViewport,
        loadPage: null),
      () => _logExportCoordinator?.ExportVisible(),
      _logViewport.ScrollToTop,
      _logViewport.ScrollToBottom,
      () => JumpToHistoryBoundary(LogViewRefreshKind.HistoryStart),
      () => JumpToHistoryBoundary(LogViewRefreshKind.HistoryEnd),
      CloseTransientLogLayer);
  }

  private void JumpToHistoryBoundary(LogViewRefreshKind boundary)
  {
    if (_disposed || !_logPageActive || _logController is null)
      return;

    _pendingHistoryBoundary = boundary;
    _logViewport.SetFollowLatest(false);
    ScheduleLogRefresh(boundary, forceRedraw: true);
  }

  private void ApplyPendingHistoryBoundary()
  {
    var boundary = _pendingHistoryBoundary;
    _pendingHistoryBoundary = null;
    if (boundary == LogViewRefreshKind.HistoryStart)
      _logViewport.ScrollToTop();
    else if (boundary == LogViewRefreshKind.HistoryEnd)
      _logViewport.ScrollToBottom();
  }

  private void CloseTransientLogLayer()
  {
    if (_correlationCoordinator.IsActive)
    {
      ExitCorrelationView();
      return;
    }

    if (_sessionCoordinator.IsActive)
      ExitSessionView();
  }
}
