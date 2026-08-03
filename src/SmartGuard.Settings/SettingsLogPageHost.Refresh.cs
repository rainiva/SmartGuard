using System.Windows;
using System.Windows.Controls;

namespace SmartGuard.Settings;

internal sealed partial class SettingsLogPageHost
{
  private void EnsureLogViewInitialized()
  {
    if (_disposed || _logViewInitialized || _logPath is null)
      return;

    _logViewInitialized = true;
    var controls = SettingsLogPageInitializer.Initialize(
      _window,
      _logPath,
      _fallbackLogPath,
      AddLogTagFilter,
      () =>
      {
        EnsureLogScrollViewerHooked();
        _logViewport.ApplyFollowLatestIfEnabled();
      },
      () => ScheduleLogRefresh(LogViewRefreshKind.Newer));
    _logController = controls.Controller;
    _logSearchFilterBar = controls.SearchFilterBar;
    _lstLogView = controls.ListView;
    _logListPresenter = controls.ListPresenter;
    _lblLogStatus = controls.StatusLabel;
    _logTopLoadingIndicator = _window.FindName("logTopLoadingIndicator") as Border;
    _chkLogFollowTail = controls.FollowTailCheckBox;
    _cmbLogTimeRange = controls.TimeRangeComboBox;
    _panelLogCustomRange = controls.CustomRangePanel;
    _txtLogRangeStart = controls.RangeStartTextBox;
    _txtLogRangeEnd = controls.RangeEndTextBox;
    _chkLogSearchCaseSensitive = controls.CaseSensitiveCheckBox;
    _logTimer = controls.RefreshTimer;
    _logExportCoordinator = new SettingsLogExportCoordinator(
      _window,
      _root,
      _toastService,
      () => _refreshPipeline.UiRefreshInFlight,
      () => _logController,
      () => _logSearchFilterBar,
      () => _chkLogSearchCaseSensitive,
      () => _cmbLogTimeRange,
      () => _txtLogRangeStart,
      () => _txtLogRangeEnd,
      () => _correlationCoordinator,
      _logPath,
      _fallbackLogPath);

    SettingsLogPageHostWiring.WireToolbarButtons(
      _window,
      _chkLogFollowTail,
      _logViewport,
      () => _logExportCoordinator.CopyVisible(),
      () => _logExportCoordinator.ExportVisible(),
      () => _logExportCoordinator.OpenFolder(),
      ForceRefreshLogView);
    SettingsLogPageHostWiring.WireSearchAndFilterEvents(
      _logSearchFilterBar,
      _searchCoordinator,
      _cmbLogTimeRange,
      _panelLogCustomRange,
      _txtLogRangeStart,
      _txtLogRangeEnd,
      _chkLogSearchCaseSensitive,
      () => OnFilterRefresh(),
      _logListPresenter,
      _logViewport);
    if (_lstLogView is not null && _logListPresenter is not null)
    {
      SettingsLogPageHostWiring.WireExceptionFoldEvents(_lstLogView, _logListPresenter);
      SettingsLogPageHostWiring.WireCorrelationEvents(
        _lstLogView,
        item => EnterCorrelationView(item.SourceEvent));
    }

    WireCorrelationUi();
    _logViewport.SyncFollowLatestFromUi();
    if (_logController is not null)
      _logController.FollowTail = _logViewport.FollowLatest;
  }

  internal void RefreshLogView(bool forceRedraw = false)
  {
    if (_disposed)
      return;

    ScheduleLogRefresh(LogViewRefreshKind.Newer, forceRedraw);
  }

  private void OnFilterRefresh(bool forceRedraw = false)
  {
    _refreshPipeline.NotifyFilterGenerationChanged();
    ScheduleLogRefresh(LogViewRefreshKind.Refilter, forceRedraw);
  }

  private void ScheduleLogRefresh(
    LogViewRefreshKind kind,
    bool forceRedraw = false,
    bool forceRefresh = false) =>
    _refreshPipeline.Schedule(kind, forceRedraw, forceRefresh);

  private void ApplyRefreshResult(LogViewRefreshResult refreshResult, bool forceRedraw)
  {
    if (_disposed)
    {
      _refreshPipeline.RecordApplyAfterDispose();
      return;
    }

    if (_logController is null || _logListPresenter is null || _lblLogStatus is null)
      return;

    var forceCount = ForceRefreshLogViewCountForTests;
    SettingsLogViewRefresher.Apply(
      refreshResult,
      _logController,
      _logListPresenter,
      _lblLogStatus,
      _root,
      ref _logIdleSeconds,
      ref _logStatusMayBeStale,
      ref _lastDisplayedLines,
      ref forceCount,
      _logViewport,
      forceRedraw);
    ForceRefreshLogViewCountForTests = forceCount;
    EnsureLogAutoRefreshStarted();
  }

  internal void ForceRefreshLogView()
  {
    if (_disposed)
      return;

    _lastDisplayedLines = Array.Empty<string>();
    if (_lblLogStatus is not null)
      _lblLogStatus.Text = LoadingStatusText;
    ScheduleLogRefresh(LogViewRefreshKind.Latest, forceRedraw: true, forceRefresh: true);
  }

  private void SetTopLoadingIndicator(bool visible)
  {
    if (_logTopLoadingIndicator is null)
      return;

    _logTopLoadingIndicator.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
  }

}
