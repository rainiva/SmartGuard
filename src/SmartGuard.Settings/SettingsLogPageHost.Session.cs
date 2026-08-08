using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings;

internal sealed partial class SettingsLogPageHost
{
  internal enum SessionSelectionAction
  {
    Ignore,
    ExitSessionView,
    SelectSession,
  }

  private readonly LogSessionCoordinator _sessionCoordinator = new();
  private ComboBox? _cmbLogSession;
  private bool _sessionUiWired;
  private bool _sessionMetadataLoaded;
  private bool _suppressSessionSelectionChanged;
  private CancellationTokenSource? _sessionMetadataCancellation;
  private CancellationTokenSource? _sessionQueryCancellation;
  private int _sessionMetadataGeneration;
  private int _sessionQueryGeneration;

  internal LogSessionCoordinator SessionCoordinatorForTests => _sessionCoordinator;

  private Func<LogSessionMetadata, LogQueryExpression, CancellationToken, Task<IReadOnlyList<LogEvent>>>?
    SessionQueryOverrideForTests { get; set; }

  private void EnsureSessionControls()
  {
    _cmbLogSession ??= _window.FindName("cmbLogSession") as ComboBox;
  }

  private void WireSessionUi()
  {
    EnsureSessionControls();
    if (_cmbLogSession is not null)
      SetSessionPickerItems([LogSessionPickerItem.AllSessions()], selectedIndex: 0);

    if (_sessionUiWired)
      return;

    if (_cmbLogSession is not null)
    {
      _cmbLogSession.SelectionChanged += OnSessionComboSelectionChanged;
      _cmbLogSession.DropDownOpened += OnSessionComboDropDownOpened;
    }

    _sessionUiWired = true;
  }

  private async void LoadSessionMetadataIfNeeded()
  {
    if (_disposed || _logController is null || _cmbLogSession is null)
      return;

    EnsureSessionControls();
    if (_sessionMetadataLoaded)
      return;

    _sessionMetadataCancellation?.Cancel();
    _sessionMetadataCancellation?.Dispose();
    _sessionMetadataCancellation = new CancellationTokenSource();
    var cancellationToken = _sessionMetadataCancellation.Token;
    var generation = ++_sessionMetadataGeneration;
    SetSessionPickerItems([LogSessionPickerItem.AllSessions(), LogSessionPickerItem.Loading()], selectedIndex: 0);

    try
    {
      var sessions = await _sessionCoordinator.LoadSessionMetadataAsync(
        _logController.EventSourceForCorrelation,
        cancellationToken);

      if (!IsCurrentSessionMetadataRequest(generation, cancellationToken))
        return;

      var items = new List<LogSessionPickerItem> { LogSessionPickerItem.AllSessions() };
      if (sessions.Count == 0)
        items.Add(LogSessionPickerItem.Empty());
      else
        items.AddRange(sessions.Select(LogSessionPickerItem.ForSession));

      _sessionMetadataLoaded = true;
      var selectedIndex = 0;
      var activeSession = _sessionCoordinator.ActiveSession;
      if (activeSession is not null)
      {
        selectedIndex = items.FindIndex(
          item => item.Session is not null
                  && string.Equals(item.Session.SessionId, activeSession.SessionId, StringComparison.Ordinal));
        if (selectedIndex < 0)
        {
          SetSessionPickerItems(items, selectedIndex: 0);
          RestoreSessionView(resetComboBox: false);
          if (_lblLogStatus is not null)
            LogStatusLiveRegion.Set(_lblLogStatus, "当前运行会话已不在日志中，已恢复全部会话。");
          return;
        }
      }

      SetSessionPickerItems(items, selectedIndex);
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception)
    {
      if (!IsCurrentSessionMetadataRequest(generation, cancellationToken))
        return;

      _sessionMetadataLoaded = false;
      SetSessionPickerItems([LogSessionPickerItem.AllSessions(), LogSessionPickerItem.Error()], selectedIndex: 0);
    }
  }

  private void OnSessionComboDropDownOpened(object? sender, EventArgs e) => LoadSessionMetadataIfNeeded();

  private void OnSessionComboSelectionChanged(object sender, SelectionChangedEventArgs e) => OnSessionSelectionChanged();

  private void OnSessionSelectionChanged()
  {
    if (_disposed || _suppressSessionSelectionChanged || _cmbLogSession?.SelectedItem is not LogSessionPickerItem selected)
      return;

    switch (GetSessionSelectionAction(selected))
    {
      case SessionSelectionAction.ExitSessionView:
        ExitSessionView();
        return;
      case SessionSelectionAction.SelectSession when selected.Session is not null:
        SelectSession(selected.Session);
        return;
      default:
        SelectSessionPickerIndex(0);
        return;
    }
  }

  internal static SessionSelectionAction GetSessionSelectionAction(LogSessionPickerItem selected)
  {
    if (!selected.IsSelectable)
      return SessionSelectionAction.Ignore;

    if (selected.Kind == LogSessionPickerItemKind.All)
      return SessionSelectionAction.ExitSessionView;

    if (selected.Session is not null)
      return SessionSelectionAction.SelectSession;

    return SessionSelectionAction.Ignore;
  }

  private async void SelectSession(LogSessionMetadata session)
  {
    if (_disposed || _logController is null || _logListPresenter is null || _lblLogStatus is null)
      return;

    EnsureSessionControls();
    _sessionQueryCancellation?.Cancel();
    _sessionQueryCancellation?.Dispose();
    _sessionQueryCancellation = new CancellationTokenSource();
    var cancellationToken = _sessionQueryCancellation.Token;
    var generation = ++_sessionQueryGeneration;
    _sessionCoordinator.SelectSession(session, CaptureSessionMainViewState());
    var activeSessionId = session.SessionId;
    var sessionScope = LogSessionProjector.BuildSessionExpression(session);
    _logController.ScopeExpression = sessionScope;
    LogStatusLiveRegion.Set(_lblLogStatus, "正在加载会话事件…");

    try
    {
      var filterState = BuildCurrentLogFilterState();
      var query = _sessionCoordinator.BuildActiveQuery(filterState.QueryExpression);
      var sessionEvents = await QuerySessionEventsAsync(session, query, cancellationToken);

      if (!IsCurrentSessionQuery(generation, activeSessionId, cancellationToken))
        return;

      _logController.LoadCorrelatedEvents(sessionEvents);
      var refreshResult = _logController.CreateRefreshResult();
      ApplyRefreshResult(refreshResult, forceRedraw: true);
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception ex)
    {
      if (!IsCurrentSessionQuery(generation, activeSessionId, cancellationToken))
        return;

      RestoreSessionView(resetComboBox: true);
      LogStatusLiveRegion.Set(_lblLogStatus, $"{LoadFailedStatusText}：{ex.Message}");
    }
  }

  private void ExitSessionView()
  {
    if (_disposed)
      return;

    RestoreSessionView(resetComboBox: true);
  }

  private void RestoreSessionView(bool resetComboBox)
  {
    _sessionQueryCancellation?.Cancel();
    ++_sessionQueryGeneration;

    var restored = _sessionCoordinator.ExitSessionView();
    if (_logController is not null)
      _logController.ScopeExpression = null;
    if (resetComboBox)
      SelectSessionPickerIndex(0);

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

  private Task<IReadOnlyList<LogEvent>> QuerySessionEventsAsync(
    LogSessionMetadata session,
    LogQueryExpression query,
    CancellationToken cancellationToken)
  {
    if (SessionQueryOverrideForTests is not null)
      return SessionQueryOverrideForTests(session, query, cancellationToken);

    var service = new LogQueryService();
    return service.QueryMatchingEventsAsync(
      _logController!.EventSourceForCorrelation,
      query,
      cancellationToken);
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

    var filterState = BuildCurrentLogFilterState();

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

  private LogViewFilterState BuildCurrentLogFilterState()
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

    return LogViewFilterState.FromLegacyFields(
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

  private void SetSessionPickerItems(IReadOnlyList<LogSessionPickerItem> items, int selectedIndex)
  {
    if (_cmbLogSession is null)
      return;

    _suppressSessionSelectionChanged = true;
    try
    {
      _cmbLogSession.ItemsSource = items;
      _cmbLogSession.SelectedIndex = selectedIndex;
    }
    finally
    {
      _suppressSessionSelectionChanged = false;
    }
  }

  private void SelectSessionPickerIndex(int selectedIndex)
  {
    if (_cmbLogSession is null)
      return;

    _suppressSessionSelectionChanged = true;
    try
    {
      _cmbLogSession.SelectedIndex = selectedIndex;
    }
    finally
    {
      _suppressSessionSelectionChanged = false;
    }
  }

  private bool IsCurrentSessionMetadataRequest(int generation, CancellationToken cancellationToken)
    => !_disposed
       && !cancellationToken.IsCancellationRequested
       && generation == _sessionMetadataGeneration;

  private bool IsCurrentSessionQuery(
    int generation,
    string sessionId,
    CancellationToken cancellationToken)
    => !_disposed
       && !cancellationToken.IsCancellationRequested
       && generation == _sessionQueryGeneration
       && string.Equals(_sessionCoordinator.ActiveSession?.SessionId, sessionId, StringComparison.Ordinal);
}
