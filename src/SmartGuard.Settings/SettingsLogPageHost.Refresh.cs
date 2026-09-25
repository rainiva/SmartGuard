using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings;

internal sealed partial class SettingsLogPageHost
{
  private SettingsLogPaginationCoordinator? _logPaginationCoordinator;
  private DataGrid? _logTable;
  private ComboBox? _cmbLogPageSize;
  private TextBlock? _lblLogTotalCount;
  private ItemsControl? _logPageNumberItems;
  private bool _suppressPageSizeSelection;
  private LogMatchNavigationDirection? _pendingPagedMatchNavigation;
  private string? _pendingTableRowEventId;

  internal SettingsLogPaginationCoordinator? PaginationCoordinatorForTests => _logPaginationCoordinator;

  private void EnsureLogViewInitialized()
  {
    if (_disposed || _logViewInitialized || _logPath is null)
      return;

    _logViewInitialized = true;
    var controls = SettingsLogPageInitializer.Initialize(
      _window,
      _logPath,
      _fallbackLogPath,
      () =>
      {
        EnsureLogScrollViewerHooked();
        _logViewport.ApplyFollowLatestIfEnabled();
      },
      DetectPagedLogChanges);
    _logController = controls.Controller;
    _logPaginationCoordinator ??= new SettingsLogPaginationCoordinator(
      (intent, execute) => _refreshPipeline.SchedulePaged(intent, execute),
      ExecutePagedQueryAsync,
      () => _disposed);
    _refreshPipeline.ConfigurePagedApply(ApplyPagedModel);
    _logTable = _window.FindName("logTable") as DataGrid;
    _cmbLogPageSize = _window.FindName("cmbLogPageSize") as ComboBox;
    _lblLogTotalCount = _window.FindName("lblLogTotalCount") as TextBlock;
    _logPageNumberItems = _window.FindName("logPageNumberItems") as ItemsControl;
    if (_logTable is not null)
    {
      _logTable.ItemsSource = _logTablePresenter.Rows;
      _logTable.SelectionMode = DataGridSelectionMode.Single;
      _logTable.SelectionUnit = DataGridSelectionUnit.FullRow;
      SettingsLogPageHostWiring.WireLogTableSorting(
        _logTable,
        () => _logPaginationCoordinator.ToggleSortAsync(),
        () => _logPaginationCoordinator.State.IsScanComplete);
      if (_window.FindName("chkLogWrap") is CheckBox wrapToggle)
        SettingsLogPageHostWiring.WireLogTableWrapMode(_logTable, wrapToggle);
      SettingsLogPageHostWiring.WireLogTableExceptionFold(_logTable);
      SettingsLogPageHostWiring.WireLogTableCorrelationEvents(
        _logTable,
        row => EnterCorrelationView(row.SourceEvent));
    }
    SettingsLogPageHostWiring.WirePaginationCommands(
      _window,
      () => _logPaginationCoordinator.State,
      () => _logPaginationCoordinator.State.CanGoFirst
        ? _logPaginationCoordinator.GoToPageAsync(1)
        : Task.CompletedTask,
      () => _logPaginationCoordinator.State.CanGoPrevious
        ? _logPaginationCoordinator.GoToPageAsync(_logPaginationCoordinator.State.CurrentPage - 1)
        : Task.CompletedTask,
      pageNumber => _logPaginationCoordinator.State.CanNavigateToPage(pageNumber)
        ? _logPaginationCoordinator.GoToPageAsync(pageNumber)
        : Task.CompletedTask,
      () => _logPaginationCoordinator.State.CanGoNext
        ? _logPaginationCoordinator.GoToPageAsync(_logPaginationCoordinator.State.CurrentPage + 1)
        : Task.CompletedTask,
      () => _logPaginationCoordinator.State.CanGoLast
        ? _logPaginationCoordinator.GoToPageAsync(_logPaginationCoordinator.State.TotalPages)
        : Task.CompletedTask);
    _searchCoordinator.MatchGenerationChanged += OnLogMatchGenerationChanged;
    _logSearchFilterBar = controls.SearchFilterBar;
    _lstLogView = controls.ListView;
    _logListPresenter = controls.ListPresenter;
    _lblLogStatus = controls.StatusLabel;
    _logTopLoadingIndicator = _window.FindName("logTopLoadingIndicator") as Border;
    _chkLogFollowTail = controls.FollowTailCheckBox;
    _cmbLogTimeRange = controls.TimeRangeComboBox;
    _cmbLogLevel = controls.LevelFilterComboBox;
    _cmbLogSource = controls.ComponentFilterComboBox;
    _panelLogCustomRange = controls.CustomRangePanel;
    _txtLogRangeStart = controls.RangeStartTextBox;
    _txtLogRangeEnd = controls.RangeEndTextBox;
    if (_txtLogRangeStart is not null
        && _window.FindName("dateLogRangeStart") is DatePicker startDate
        && _window.FindName("txtLogRangeStartTime") is TextBox startTime)
      SettingsLogPageHostWiring.WireLogDateTimePicker(startDate, startTime, _txtLogRangeStart);
    if (_txtLogRangeEnd is not null
        && _window.FindName("dateLogRangeEnd") is DatePicker endDate
        && _window.FindName("txtLogRangeEndTime") is TextBox endTime)
      SettingsLogPageHostWiring.WireLogDateTimePicker(endDate, endTime, _txtLogRangeEnd);
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
      _fallbackLogPath,
      () => _logTablePresenter,
      () => _logPaginationCoordinator.CurrentIndex,
      () => _logController?.EventSourceForCorrelation,
      () => _logPaginationCoordinator.SortDirection,
      () => _logPaginationCoordinator.State.PageSize);

    SettingsLogPageHostWiring.WireToolbarButtons(
      _window,
      _chkLogFollowTail,
      _logViewport,
      () => _logExportCoordinator.CopyCurrentPage(),
      () => _logExportCoordinator.ExportAllMatches(),
      () => _logExportCoordinator.OpenFolder(),
      ForceRefreshLogViewFromToolbar);
    SettingsLogPageHostWiring.WirePagedLogActions(
      _window,
      ToggleLogFilterPanel,
      pageSize => _logPaginationCoordinator.ChangePageSizeAsync(pageSize),
      () => _logExportCoordinator.ExportCurrentPage(),
      () => !_suppressPageSizeSelection);
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
      _logViewport,
      levelFilterComboBox: _cmbLogLevel,
      componentFilterComboBox: _cmbLogSource);
    WireLogWorkbenchCommands();
    if (_lstLogView is not null && _logListPresenter is not null)
    {
      SettingsLogPageHostWiring.WireExceptionFoldEvents(_lstLogView, _logListPresenter);
      SettingsLogPageHostWiring.WireCorrelationEvents(
        _lstLogView,
        item => EnterCorrelationView(item.SourceEvent));
    }

    WireCorrelationUi();
    WireSessionUi();
    _logViewport.SyncFollowLatestFromUi();
    if (_logController is not null)
      _logController.FollowTail = _logViewport.FollowLatest;
  }

  internal void RefreshLogView(bool forceRedraw = false)
  {
    if (_disposed)
      return;

    if (IsPagedLogSource())
    {
      RequestPagedQuery(captureNewSnapshot: forceRedraw);
      return;
    }

    ScheduleLogRefresh(LogViewRefreshKind.Newer, forceRedraw);
  }

  private void OnFilterRefresh(bool forceRedraw = false)
  {
    _refreshPipeline.NotifyFilterGenerationChanged();
    if (_logPageActive && IsPagedLogSource())
    {
      RequestPagedQuery(captureNewSnapshot: false);
      return;
    }

    ScheduleLogRefresh(LogViewRefreshKind.Refilter, forceRedraw);
  }

  private void OnLogMatchGenerationChanged(int _)
    => _logPaginationCoordinator?.MarkSnapshotStale();

  private bool IsPagedLogSource()
    => _logController?.EventSourceForCorrelation is ILogSnapshotPhysicalLineSource
       && _logPaginationCoordinator is not null;

  private LogQueryExpression BuildPaginationExpression()
  {
    if (_logController is null)
      return LogQueryMatchAllExpression.Instance;

    SettingsLogSearchCoordinator.ApplyFilters(
      _logController,
      _logSearchFilterBar,
      _chkLogSearchCaseSensitive,
      _cmbLogTimeRange,
      _txtLogRangeStart,
      _txtLogRangeEnd,
      _cmbLogLevel,
      _cmbLogSource);

    var filterState = LogViewFilterState.FromLegacyFields(
      _logController.SearchKeyword,
      _logController.SearchCaseSensitive,
      _logController.ActiveTagFilters,
      _logController.TimeRange,
      _logController.CustomRangeStart,
      _logController.CustomRangeEnd,
      _logController.OperationIdFilter,
      _logController.EventNameFilter,
      _logController.ProcessIdFilter,
      _logController.ThreadIdFilter,
      _logController.NowProvider,
      _logController.ComponentFilter);
    _pagedHasActiveFilters = filterState.HasActiveFilters;
    var parts = new List<LogQueryExpression> { filterState.QueryExpression };
    foreach (var tag in filterState.ActiveTagFilters)
      parts.Add(LogTagFilterCatalog.BuildQueryExpression(tag));

    if (_logController.ScopeExpression is { } scope
        && scope is not LogQueryMatchAllExpression)
    {
      _pagedHasActiveFilters = true;
      parts.Add(scope);
    }

    return parts.Count == 1 ? parts[0] : LogQueryExpression.AndAll(parts);
  }

  private void RequestPagedQuery(
    bool captureNewSnapshot,
    LogSortDirection? sortDirection = null,
    int? restorePage = null,
    string? restoreSelectedEventId = null)
  {
    if (!_logPageActive || !IsPagedLogSource() || _logController is null || _logPaginationCoordinator is null)
      return;

    _pendingPageRestore = restorePage is > 1 ? restorePage : null;
    _pendingTableRowEventId = restoreSelectedEventId;
    var expression = BuildPaginationExpression();
    var scope = new LogReadScope(Array.Empty<LogSourceIdentity>());
    var ignoreCase = !_logController.SearchCaseSensitive;
    if (sortDirection is not null)
    {
      _ = _logPaginationCoordinator.ApplyFilterAsync(expression, scope, ignoreCase, sortDirection);
    }
    else if (captureNewSnapshot)
    {
      _ = _logPaginationCoordinator.RefreshAsync(expression, scope, ignoreCase);
    }
    else
    {
      _ = _logPaginationCoordinator.ApplyFilterAsync(expression, scope, ignoreCase);
    }
  }

  private void DetectPagedLogChanges()
  {
    if (!_logPageActive || !IsPagedLogSource() || _logPaginationCoordinator?.CurrentSnapshot is not { } snapshot)
      return;

    try
    {
      var source = (ILogSnapshotPhysicalLineSource)_logController!.EventSourceForCorrelation;
      var current = source.CaptureSnapshot();
      if (string.Equals(current.Fingerprint, snapshot.Fingerprint, StringComparison.Ordinal))
      {
        RefreshPagedIdleStatus();
        return;
      }

      if (_refreshPipeline.UiRefreshInFlight)
        return;

      if (_logPaginationCoordinator.State.CurrentPage == 1
          && _logPaginationCoordinator.SortDirection == LogSortDirection.NewestFirst)
      {
        RequestPagedQuery(captureNewSnapshot: true);
        return;
      }

      _logPaginationCoordinator.MarkSnapshotStale();
      if (_lblLogStatus is not null)
        LogStatusLiveRegion.Set(_lblLogStatus, "有新日志，点击刷新查看。");
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
  }

  private void ApplyPagedModel(LogPaginationApplyModel model)
  {
    if (_disposed || _logPaginationCoordinator is null)
      return;

    _logPaginationCoordinator.ApplyModel(model);
    if (model.ErrorMessage is not null)
    {
      if (_lblLogStatus is not null)
        LogStatusLiveRegion.Set(_lblLogStatus, $"{LoadFailedStatusText}：{model.ErrorMessage}");
      UpdatePagerControls();
      return;
    }

    _logTablePresenter.ApplyPage(model.Events, model.PageSize);
    _logListPresenter?.ApplyDelta(LogWindowDelta.Initialize(model.Events));
    _logController?.LoadCorrelatedEvents(model.Events);
    UpdatePagerControls();
    if (_lblLogStatus is not null && model.IsProvisional)
      LogStatusLiveRegion.Set(_lblLogStatus, "正在统计匹配日志…");

    if (!model.IsProvisional && model.ExactTotalCount is int exactTotalCount)
    {
      if (_pagedHasActiveFilters && exactTotalCount == 0)
        _logListPresenter?.Apply(LogViewUpdatePlan.ReplaceAll(["无匹配结果"]));

      ApplyPagedStatus(model, exactTotalCount);
      _lastDisplayedLines = model.Events
        .Select(LogViewEventFilter.GetDisplayLine)
        .ToArray();
      EnsureLogAutoRefreshStarted();
    }

    var applyRestoredTableSelection = !model.IsProvisional;
    if (!model.IsProvisional && _pendingPageRestore is int restorePage)
    {
      _pendingPageRestore = null;
      if (restorePage != _logPaginationCoordinator.State.CurrentPage
          && restorePage <= _logPaginationCoordinator.State.TotalPages)
      {
        applyRestoredTableSelection = false;
        _ = _logPaginationCoordinator.GoToPageAsync(restorePage);
      }
    }

    if (applyRestoredTableSelection)
      ApplyPendingRestoredTableSelection();

    ApplyPendingPagedMatchNavigation();
  }

  private int? _pendingPageRestore;

  private void ApplyPagedStatus(LogPaginationApplyModel model, int exactTotalCount)
    => ApplyPagedStatus(model.Events, exactTotalCount, announce: true);

  private void RefreshPagedIdleStatus()
  {
    if (_logPaginationCoordinator?.State.ExactTotalCount is not int exactTotalCount)
      return;

    ApplyPagedStatus(
      _logTablePresenter.Rows.Select(row => row.SourceEvent).ToArray(),
      exactTotalCount,
      announce: false);
  }

  private void ApplyPagedStatus(
    IReadOnlyList<LogEvent> events,
    int exactTotalCount,
    bool announce)
  {
    if (_lblLogStatus is null || _logController is null)
      return;

    var idleRead = LogViewIdleReader.TryRead(_root);
    if (idleRead.Seconds is int seconds)
      _logIdleSeconds = seconds;
    _logStatusMayBeStale = idleRead.StatusMayBeStale;

    var snapshot = new LogViewSnapshot(
      events.Select(LogViewEventFilter.GetDisplayLine).ToArray(),
      exactTotalCount,
      IsTailTruncated: false,
      LogPath: _logPath ?? string.Empty,
      ContentChanged: true,
      SearchKeyword: _logController.SearchKeyword,
      EmptyStateMessage: _pagedHasActiveFilters && exactTotalCount == 0 ? "无匹配结果" : null,
      TimeRange: _logController.TimeRange,
      MatchedLineCount: exactTotalCount,
      ActiveTagFilters: _logController.ActiveTagFilters);
    var status = LogViewStatusTextBuilder.Build(
      snapshot,
      DateTime.Now,
      _logIdleSeconds,
      _logStatusMayBeStale);
    if (_pagedHasActiveFilters && exactTotalCount == 0)
      status = $"{status} | 无匹配结果";

    if (announce)
      LogStatusLiveRegion.Set(_lblLogStatus, status);
    else
      _lblLogStatus.Text = status;
  }

  private void ApplyPendingRestoredTableSelection()
  {
    if (string.IsNullOrWhiteSpace(_pendingTableRowEventId))
      return;

    var eventId = _pendingTableRowEventId;
    _pendingTableRowEventId = null;
    SelectTableRow(eventId);
  }

  private void UpdatePagerControls()
  {
    if (_logPaginationCoordinator is null)
      return;

    var state = _logPaginationCoordinator.State;
    if (_lblLogTotalCount is not null)
    {
      _lblLogTotalCount.Text = state.ExactTotalCount is int exactCount
        ? $"共 {exactCount:N0} 条日志"
        : "正在统计日志…";
    }

    if (_cmbLogPageSize is not null)
    {
      _suppressPageSizeSelection = true;
      try
      {
        _cmbLogPageSize.SelectedIndex = state.PageSize switch
        {
          50 => 1,
          100 => 2,
          _ => 0,
        };
      }
      finally
      {
        _suppressPageSizeSelection = false;
      }
    }

    RenderPagerTokens(state);
    CommandManager.InvalidateRequerySuggested();
  }

  private void RenderPagerTokens(LogPaginationState state)
  {
    if (_logPageNumberItems is null)
      return;

    _logPageNumberItems.Items.Clear();
    var buttonStyle = _window.FindResource("LogToolbarCompactButton") as Style;
    var currentButtonStyle = _window.FindResource("LogPagerCurrentButton") as Style;
    foreach (var token in LogPagerTokenBuilder.Build(state.CurrentPage, state.TotalPages))
    {
      if (token is LogPageEllipsisToken)
      {
        _logPageNumberItems.Items.Add(new TextBlock
        {
          Text = "…",
          Focusable = false,
          IsHitTestVisible = false,
          Foreground = _window.FindResource("TextSecondary") as System.Windows.Media.Brush,
          VerticalAlignment = VerticalAlignment.Center,
          Margin = new Thickness(0, 0, 8, 0),
        });
        continue;
      }

      var number = (LogPageNumberToken)token;
      var button = new Button
      {
        Content = number.PageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Style = number.IsCurrent ? currentButtonStyle : buttonStyle,
        Command = LogPaginationCommands.GoToPage,
        CommandParameter = number.PageNumber,
        IsEnabled = !number.IsCurrent,
        Margin = new Thickness(0, 0, 4, 0),
      };
      AutomationProperties.SetName(button, $"第 {number.PageNumber} 页");
      _logPageNumberItems.Items.Add(button);
    }
  }

  private void ApplyPendingPagedMatchNavigation()
  {
    if (_pendingPagedMatchNavigation is not LogMatchNavigationDirection direction
        || _logPaginationCoordinator is null)
      return;

    _pendingPagedMatchNavigation = null;
    var result = LogPagedMatchNavigator.Resolve(
      _logTablePresenter.Rows,
      _logSearchFilterBar?.Keyword,
      direction == LogMatchNavigationDirection.Forward ? -1 : _logTablePresenter.Rows.Count,
      direction == LogMatchNavigationDirection.Forward,
      _logPaginationCoordinator.State);
    if (result.RowIndex is int rowIndex && _logTable is not null)
    {
      _logTable.SelectedIndex = rowIndex;
      _logViewport.BringTableSelectionIntoView(_logTable, _logTable.SelectedItem);
    }
  }

  private void SelectTableRow(string eventId)
  {
    if (_logTable is null)
      return;

    var index = -1;
    for (var rowIndex = 0; rowIndex < _logTablePresenter.Rows.Count; rowIndex++)
    {
      if (string.Equals(_logTablePresenter.Rows[rowIndex].EventId, eventId, StringComparison.Ordinal))
      {
        index = rowIndex;
        break;
      }
    }
    if (index < 0 || index >= _logTablePresenter.Rows.Count)
      return;

    _logTable.SelectedIndex = index;
    _logViewport.BringTableSelectionIntoView(_logTable, _logTable.SelectedItem);
  }

  private void ToggleLogFilterPanel()
  {
    if (_window.FindName("logFilterPopup") is not Popup popup)
      return;

    if (popup.IsOpen)
    {
      popup.IsOpen = false;
      return;
    }

    ConfigureLogFilterPopup(_window, popup);
    TrackLogFilterPopupWhileOpen(_window, popup);
    popup.IsOpen = true;
    ConfigureLogFilterPopup(_window, popup);
  }

  internal static LogFilterPopupTracking TrackLogFilterPopupWhileOpen(Window window, Popup popup)
  {
    ArgumentNullException.ThrowIfNull(window);
    ArgumentNullException.ThrowIfNull(popup);
    return new LogFilterPopupTracking(window, popup);
  }

  internal sealed class LogFilterPopupTracking : IDisposable
  {
    private readonly Window _window;
    private readonly Popup _popup;

    internal LogFilterPopupTracking(Window window, Popup popup)
    {
      _window = window;
      _popup = popup;
      _window.LocationChanged += OnWindowLocationChanged;
      _window.SizeChanged += OnWindowSizeChanged;
      _window.StateChanged += OnWindowStateChanged;
      _popup.Closed += OnPopupClosed;
      IsAttached = true;
    }

    internal bool IsAttached { get; private set; }

    internal int RepositionCount { get; private set; }

    public void Dispose()
    {
      if (!IsAttached)
        return;

      _window.LocationChanged -= OnWindowLocationChanged;
      _window.SizeChanged -= OnWindowSizeChanged;
      _window.StateChanged -= OnWindowStateChanged;
      _popup.Closed -= OnPopupClosed;
      IsAttached = false;
    }

    private void OnWindowLocationChanged(object? sender, EventArgs e) => QueueReposition();

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) => QueueReposition();

    private void OnWindowStateChanged(object? sender, EventArgs e) => QueueReposition();

    private void OnPopupClosed(object? sender, EventArgs e) => Dispose();

    private void QueueReposition()
    {
      if (!IsAttached || !_popup.IsOpen)
        return;

      _ = _window.Dispatcher.BeginInvoke(
        System.Windows.Threading.DispatcherPriority.Loaded,
        new Action(() =>
        {
          if (!IsAttached || !_popup.IsOpen)
            return;

          ConfigureLogFilterPopup(_window, _popup);
          var originalOffset = _popup.HorizontalOffset;
          _popup.HorizontalOffset = originalOffset + 0.1;
          _popup.HorizontalOffset = originalOffset;
          RepositionCount++;
        }));
    }
  }

  internal static void ConfigureLogFilterPopup(Window window, Popup popup)
  {
    ArgumentNullException.ThrowIfNull(window);
    ArgumentNullException.ThrowIfNull(popup);

    if (window.FindName("btnLogFilter") is not Button filterButton
        || window.FindName("rightContentRoot") is not FrameworkElement contentRoot
        || window.FindName("logFilterPanel") is not FrameworkElement panel)
      return;

    popup.PlacementTarget = filterButton;
    popup.Placement = PlacementMode.Custom;
    popup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
    {
      var contentOrigin = contentRoot.TranslatePoint(new Point(0, 0), filterButton);
      // WPF 与回调交换的 popupSize/targetSize 及返回点共用同一坐标系；
      // 在 >100% DPI 的缩放显示器上该坐标系是设备像素（例如 200% 时
      // targetSize.Width = 2 × 按钮 DIP 宽度）。从目标的实际尺寸自校准
      // 设备比例，使客户区夹紧计算与返回点保持同一单位，否则返回的
      // DIP 点会被按设备像素解释，弹窗被推向客户区之外。
      var deviceScale = filterButton.ActualWidth > 0 && targetSize.Width > 0
        ? targetSize.Width / filterButton.ActualWidth
        : 1.0;
      var measuredPopupWidth = double.IsNaN(panel.Width)
        ? popupSize.Width
        : panel.Width * deviceScale;
      return CreateLogFilterPopupPlacements(
        new Size(measuredPopupWidth, popupSize.Height),
        targetSize,
        offset,
        contentOrigin.X * deviceScale,
        contentRoot.ActualWidth * deviceScale,
        contentOrigin.Y * deviceScale,
        contentRoot.ActualHeight * deviceScale,
        FilterPopupVerticalGap * deviceScale);
    };

    if (contentRoot.ActualWidth > 0)
    {
      // 只封顶不强制宽度：弹窗按内容自适应宽度，固定 Width 会在组合框
      // 右侧留下大面积空白；窄窗口时由 MaxWidth 收缩并依靠内部滚动。
      panel.MaxWidth = Math.Min(panel.MaxWidth, contentRoot.ActualWidth);
    }
    if (contentRoot.ActualHeight > 0)
      panel.MaxHeight = Math.Max(1, contentRoot.ActualHeight - 16);
  }

  /// <summary>
  /// 弹窗与筛选按钮之间的固定间距（DIP）。间距进入定位偏移而不是
  /// <c>logFilterPanel</c> 的 Border Margin，避免边距被漏算出客户区。
  /// 调用方须按与 <paramref name="targetSize"/> 相同的单位传入。
  /// </summary>
  internal const double FilterPopupVerticalGap = 8;

  internal static CustomPopupPlacement[] CreateLogFilterPopupPlacements(
    Size popupSize,
    Size targetSize,
    Point offset,
    double containerLeft = double.NegativeInfinity,
    double containerWidth = double.PositiveInfinity,
    double containerTop = double.NegativeInfinity,
    double containerHeight = double.PositiveInfinity,
    double verticalGap = FilterPopupVerticalGap)
  {
    var alignedLeft = targetSize.Width - popupSize.Width;
    if (double.IsFinite(containerLeft) && double.IsFinite(containerWidth))
    {
      var maximumLeft = containerLeft + containerWidth - popupSize.Width;
      alignedLeft = maximumLeft < containerLeft
        ? containerLeft
        : Math.Clamp(alignedLeft, containerLeft, maximumLeft);
    }

    alignedLeft += offset.X;

    var clampVertically = double.IsFinite(containerTop) && double.IsFinite(containerHeight);
    var maximumTop = clampVertically
      ? Math.Max(containerTop, containerTop + containerHeight - popupSize.Height)
      : double.PositiveInfinity;
    var belowTop = targetSize.Height + offset.Y + verticalGap;
    var aboveTop = -popupSize.Height + offset.Y - verticalGap;
    if (clampVertically)
    {
      belowTop = Math.Clamp(belowTop, containerTop, maximumTop);
      aboveTop = Math.Clamp(aboveTop, containerTop, maximumTop);
    }

    return
    [
      new CustomPopupPlacement(
        new Point(alignedLeft, belowTop),
        PopupPrimaryAxis.Horizontal),
      new CustomPopupPlacement(
        new Point(alignedLeft, aboveTop),
        PopupPrimaryAxis.Horizontal),
    ];
  }

  private async Task ExecutePagedQueryAsync(
    LogPaginationIntent intent,
    LogSourceSnapshot? snapshot,
    LogQueryPageIndex? index,
    IProgress<LogPaginationApplyModel> progress,
    CancellationToken cancellationToken)
  {
    var eventSource = _logController?.EventSourceForCorrelation
      ?? throw new InvalidOperationException("日志控制器尚未初始化。");
    if (eventSource is not ILogSnapshotPhysicalLineSource snapshotSource)
      throw new InvalidOperationException("分页日志源不支持固定 snapshot。");

    var retryIntent = intent;
    var retrySnapshot = snapshot;
    var retryIndex = index;
    LogSourceSnapshot? lastAttemptSnapshot = snapshot;
    for (var attempt = 0; attempt < 2; attempt++)
    {
      try
      {
        var queryService = new LogQueryService();
        var effectiveSnapshot = retryIntent.CaptureNewSnapshot || retrySnapshot is null
          ? snapshotSource.CaptureSnapshot()
          : retrySnapshot;
        lastAttemptSnapshot = effectiveSnapshot;
        var preview = await queryService.ReadPreviewPageAsync(
          eventSource,
          retryIntent.Expression,
          retryIntent.Scope,
          effectiveSnapshot,
          retryIntent.PageSize,
          retryIntent.IgnoreCase,
          cancellationToken).ConfigureAwait(false);
        progress.Report(new LogPaginationApplyModel(
          retryIntent.QueryGeneration,
          IsProvisional: true,
          preview,
          retryIntent.PageNumber,
          retryIntent.PageSize,
          ExactTotalCount: null,
          IsScanComplete: false,
          retryIntent.SortDirection,
          effectiveSnapshot,
          retryIndex,
          ErrorMessage: null));

        var effectiveIndex = retryIndex ?? await queryService.BuildPageIndexAsync(
          eventSource,
          retryIntent.Expression,
          retryIntent.Scope,
          effectiveSnapshot,
          retryIntent.IgnoreCase,
          progress: null,
          cancellationToken).ConfigureAwait(false);
        var page = await queryService.ReadPageAsync(
          eventSource,
          effectiveIndex,
          new LogQueryPageRequest(retryIntent.PageNumber, retryIntent.PageSize, retryIntent.SortDirection),
          cancellationToken).ConfigureAwait(false);
        progress.Report(new LogPaginationApplyModel(
          retryIntent.QueryGeneration,
          IsProvisional: false,
          page.Events,
          page.PageNumber,
          page.PageSize,
          page.ExactTotalCount,
          IsScanComplete: true,
          page.SortDirection,
          effectiveSnapshot,
          effectiveIndex,
          ErrorMessage: null));
        return;
      }
      catch (LogSnapshotInvalidatedException) when (attempt == 0 && !cancellationToken.IsCancellationRequested)
      {
        retryIntent = intent with { CaptureNewSnapshot = true };
        retrySnapshot = null;
        retryIndex = null;
      }
      catch (LogSnapshotInvalidatedException ex) when (!cancellationToken.IsCancellationRequested)
      {
        progress.Report(new LogPaginationApplyModel(
          intent.QueryGeneration,
          IsProvisional: false,
          Events: Array.Empty<LogEvent>(),
          intent.PageNumber,
          intent.PageSize,
          ExactTotalCount: null,
          IsScanComplete: false,
          intent.SortDirection,
          lastAttemptSnapshot ?? new LogSourceSnapshot(Array.Empty<LogSourceSnapshotEntry>(), "invalidated"),
          Index: null,
          ErrorMessage: ex.Message));
        return;
      }
    }
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
    ApplyPendingHistoryBoundary();
    EnsureLogAutoRefreshStarted();
  }

  internal void ForceRefreshLogView()
  {
    if (_disposed)
      return;

    _lastDisplayedLines = Array.Empty<string>();
    if (_lblLogStatus is not null)
      LogStatusLiveRegion.Set(_lblLogStatus, LoadingStatusText);

    if (IsPagedLogSource())
    {
      RequestPagedQuery(captureNewSnapshot: true);
      return;
    }

    ScheduleLogRefresh(LogViewRefreshKind.Latest, forceRedraw: true, forceRefresh: true);
  }

  private void ForceRefreshLogViewFromToolbar()
  {
    _sessionMetadataLoaded = false;
    ForceRefreshLogView();
  }

  private void SetTopLoadingIndicator(bool visible)
  {
    if (_logTopLoadingIndicator is null)
      return;

    _logTopLoadingIndicator.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
  }

}
