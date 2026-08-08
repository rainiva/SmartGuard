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
      () => NavigateMatch(LogMatchNavigationDirection.Forward),
      () => NavigateMatch(LogMatchNavigationDirection.Backward),
      () => _logExportCoordinator?.ExportVisible(),
      _logViewport.ScrollToTop,
      _logViewport.ScrollToBottom,
      () => JumpToHistoryBoundary(LogViewRefreshKind.HistoryStart),
      () => JumpToHistoryBoundary(LogViewRefreshKind.HistoryEnd),
      CloseTransientLogLayer);
  }

  private void NavigateMatch(LogMatchNavigationDirection direction)
  {
    if (!IsPagedLogSource() || _logPaginationCoordinator is null)
    {
      _searchCoordinator.NavigateMatch(
        direction,
        _logListPresenter,
        _logViewport,
        loadPage: null);
      return;
    }

    var result = LogPagedMatchNavigator.Resolve(
      _logTablePresenter.Rows,
      _logSearchFilterBar?.Keyword,
      _logTable?.SelectedIndex ?? -1,
      direction == LogMatchNavigationDirection.Forward,
      _logPaginationCoordinator.State);
    if (result.RowIndex is int rowIndex && _logTable is not null)
    {
      _logTable.SelectedIndex = rowIndex;
      _logViewport.BringTableSelectionIntoView(_logTable, _logTable.SelectedItem);
      return;
    }

    if (result.TargetPage is int targetPage)
    {
      _pendingPagedMatchNavigation = direction;
      _ = _logPaginationCoordinator.GoToPageAsync(targetPage);
    }
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
