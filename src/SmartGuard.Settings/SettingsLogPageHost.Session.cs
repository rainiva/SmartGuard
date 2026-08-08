using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings;

internal sealed partial class SettingsLogPageHost
{
  private readonly LogSessionCoordinator _sessionCoordinator = new();
  private Expander? _logSessionInspector;
  private ComboBox? _cmbLogSession;
  private Border? _logSessionBanner;
  private TextBlock? _lblLogSessionTitle;
  private Button? _btnLogSessionBack;

  internal LogSessionCoordinator SessionCoordinatorForTests => _sessionCoordinator;

  private void EnsureSessionControls()
  {
    _logSessionInspector ??= _window.FindName("logSessionInspector") as Expander;
    _cmbLogSession ??= _window.FindName("cmbLogSession") as ComboBox;
    _logSessionBanner ??= _window.FindName("logSessionBanner") as Border;
    _lblLogSessionTitle ??= _window.FindName("lblLogSessionTitle") as TextBlock;
    _btnLogSessionBack ??= _window.FindName("btnLogSessionBack") as Button;
  }

  private void WireSessionUi()
  {
    EnsureSessionControls();
    if (_btnLogSessionBack is not null)
      _btnLogSessionBack.Click += (_, _) => ExitSessionView();

    if (_cmbLogSession is not null)
      _cmbLogSession.SelectionChanged += (_, _) => OnSessionSelectionChanged();

    if (_logSessionInspector is not null)
      _logSessionInspector.Expanded += (_, _) => LoadSessionMetadataIfNeeded();
  }

  private async void LoadSessionMetadataIfNeeded()
  {
    if (_disposed || _logController is null || _cmbLogSession is null)
      return;

    EnsureSessionControls();
    if (_cmbLogSession.Items.Count > 0)
      return;

    try
    {
      var sessions = await _sessionCoordinator.LoadSessionMetadataAsync(
        _logController.EventSourceForCorrelation,
        CancellationToken.None);

      _cmbLogSession.DisplayMemberPath = nameof(LogSessionPickerItem.Title);
      _cmbLogSession.ItemsSource = sessions
        .Select(LogSessionPickerItem.ForSession)
        .ToList();
    }
    catch
    {
      // Session metadata is optional; leave the inspector empty on failure.
    }
  }

  private void OnSessionSelectionChanged()
  {
    if (_disposed || _cmbLogSession?.SelectedItem is not LogSessionPickerItem selected)
      return;

    if (!selected.IsSelectable || selected.Session is null)
      return;

    SelectSession(selected.Session);
  }

  private async void SelectSession(LogSessionMetadata session)
  {
    if (_disposed || _logController is null || _logListPresenter is null || _lblLogStatus is null)
      return;

    EnsureSessionControls();
    _sessionCoordinator.SelectSession(session, CaptureSessionMainViewState());
    LogStatusLiveRegion.Set(_lblLogStatus, "正在加载会话事件…");
    UpdateSessionBannerVisibility(true);

    try
    {
      var query = _sessionCoordinator.BuildActiveQuery();
      var service = new LogQueryService();
      var sessionEvents = await service.QueryMatchingEventsAsync(
        _logController.EventSourceForCorrelation,
        query,
        CancellationToken.None);

      _logController.LoadCorrelatedEvents(sessionEvents);
      var refreshResult = _logController.CreateRefreshResult();
      ApplyRefreshResult(refreshResult, forceRedraw: true);

      if (!string.IsNullOrWhiteSpace(_sessionCoordinator.ActiveTitle)
          && _lblLogSessionTitle is not null)
        _lblLogSessionTitle.Text = _sessionCoordinator.ActiveTitle;
    }
    catch (Exception ex)
    {
      _sessionCoordinator.ExitSessionView();
      UpdateSessionBannerVisibility(false);
      LogStatusLiveRegion.Set(_lblLogStatus, $"{LoadFailedStatusText}：{ex.Message}");
    }
  }

  private void ExitSessionView()
  {
    if (_disposed)
      return;

    var restored = _sessionCoordinator.ExitSessionView();
    UpdateSessionBannerVisibility(false);
    if (_cmbLogSession is not null)
      _cmbLogSession.SelectedItem = null;

    if (restored is null || _logController is null || _logSearchFilterBar is null)
      return;

    RestoreSessionMainViewState(restored);
    ForceRefreshLogView();

    if (restored.Anchor is not null)
      _logViewport.RestoreAnchor(restored.Anchor);

    if (!string.IsNullOrWhiteSpace(restored.SelectedEventId) && _lstLogView is not null)
    {
      var index = _logListPresenter?.FindIndexByEventId(restored.SelectedEventId) ?? -1;
      if (index >= 0)
        _lstLogView.SelectedIndex = index;
    }
  }

  private LogSessionMainViewState CaptureSessionMainViewState()
  {
    var controller = _logController
      ?? throw new InvalidOperationException("Log controller is not initialized.");

    SettingsLogSearchCoordinator.ApplyFilters(
      controller,
      _logSearchFilterBar,
      _chkLogSearchCaseSensitive,
      _cmbLogTimeRange,
      _txtLogRangeStart,
      _txtLogRangeEnd);

    var filterState = LogViewFilterState.FromLegacyFields(
      controller.SearchKeyword,
      controller.SearchCaseSensitive,
      controller.ActiveTagFilters,
      controller.TimeRange,
      controller.CustomRangeStart,
      controller.CustomRangeEnd,
      controller.OperationIdFilter,
      controller.EventNameFilter,
      controller.ProcessIdFilter,
      controller.ThreadIdFilter,
      controller.NowProvider);

    string? selectedEventId = null;
    if (_lstLogView?.SelectedItem is LogLineDisplayItem selectedItem)
      selectedEventId = selectedItem.EventId;

    return new LogSessionMainViewState(
      filterState.QueryExpression,
      _logSearchFilterBar?.Keyword ?? string.Empty,
      _logSearchFilterBar?.ActiveTags ?? [],
      controller.TimeRange,
      controller.CustomRangeStart,
      controller.CustomRangeEnd,
      controller.SearchCaseSensitive,
      _logViewport.CaptureAnchor(),
      selectedEventId);
  }

  private void RestoreSessionMainViewState(LogSessionMainViewState state)
  {
    if (_logController is null || _logSearchFilterBar is null)
      return;

    _logSearchFilterBar.SetKeyword(state.SearchKeyword);
    _logSearchFilterBar.ClearTagFilters();
    foreach (var tag in state.ActiveTags)
      _logSearchFilterBar.AddTagFilter(tag);

    _logController.SearchKeyword = state.SearchKeyword;
    _logController.ActiveTagFilters = state.ActiveTags;
    _logController.SearchCaseSensitive = state.SearchCaseSensitive;
    _logController.TimeRange = state.TimeRange;
    _logController.CustomRangeStart = state.CustomRangeStart;
    _logController.CustomRangeEnd = state.CustomRangeEnd;

    if (_cmbLogTimeRange is not null)
    {
      _cmbLogTimeRange.SelectedIndex = state.TimeRange switch
      {
        LogViewTimeRange.Today => 1,
        LogViewTimeRange.LastHour => 2,
        LogViewTimeRange.Custom => 3,
        _ => 0,
      };
    }

    if (_txtLogRangeStart is not null && state.CustomRangeStart is DateTime start)
      _txtLogRangeStart.Text = start.ToString("yyyy-MM-dd HH:mm:ss");

    if (_txtLogRangeEnd is not null && state.CustomRangeEnd is DateTime end)
      _txtLogRangeEnd.Text = end.ToString("yyyy-MM-dd HH:mm:ss");

    if (_chkLogSearchCaseSensitive is not null)
      _chkLogSearchCaseSensitive.IsChecked = state.SearchCaseSensitive;

    SettingsLogSearchCoordinator.SyncCustomRangePanelVisibility(_panelLogCustomRange, _cmbLogTimeRange);
  }

  private void UpdateSessionBannerVisibility(bool visible)
  {
    EnsureSessionControls();
    if (_logSessionBanner is null)
      return;

    _logSessionBanner.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
  }
}
