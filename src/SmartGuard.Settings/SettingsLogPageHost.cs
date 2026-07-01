using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SmartGuard.Settings;

internal sealed class SettingsLogPageHost
{
  private readonly Window _window;
  private readonly string _root;
  private readonly ToastNotificationService _toastService;

  private LogViewController? _logController;
  private System.Windows.Threading.DispatcherTimer? _logTimer;
  private System.Windows.Threading.DispatcherTimer? _logSearchDebounceTimer;
  private System.Windows.Threading.DispatcherTimer? _logCustomRangeDebounceTimer;
  private LogSearchFilterBar? _logSearchFilterBar;
  private WrapPanel? _logTagFilterLinksPanel;
  private ListBox? _lstLogView;
  private LogViewListPresenter? _logListPresenter;
  private IReadOnlyList<string> _lastDisplayedLines = Array.Empty<string>();
  private int? _logIdleSeconds;
  private bool _logStatusMayBeStale;
  private TextBlock? _lblLogStatus;
  private ScrollViewer? _logScrollViewer;
  private string? _logPath;
  private string? _fallbackLogPath;
  private CheckBox? _chkLogFollowTail;
  private bool _suppressFollowTailAutoSync;
  private bool _pendingFollowTailInitialScroll;
  private ComboBox? _cmbLogTimeRange;
  private UIElement? _panelLogCustomRange;
  private TextBox? _txtLogRangeStart;
  private TextBox? _txtLogRangeEnd;
  private CheckBox? _chkLogSearchCaseSensitive;
  private bool _logViewInitialized;
  private bool _logScrollHooked;

  internal bool IsLogViewInitializedForTests => _logViewInitialized;

  internal static int ForceRefreshLogViewCountForTests { get; private set; }

  internal static void ResetTestMetricsForTests() => ForceRefreshLogViewCountForTests = 0;

  internal SettingsLogPageHost(
    Window window,
    string root,
    ToastNotificationService toastService,
    string logPath,
    string? fallbackLogPath)
  {
    _window = window;
    _root = root;
    _toastService = toastService;
    _logPath = logPath;
    _fallbackLogPath = fallbackLogPath;
  }

  internal void ConfigurePaths(string logPath, string? fallbackLogPath)
  {
    _logPath = logPath;
    _fallbackLogPath = fallbackLogPath;
  }

  internal void AddLogTagFilter(string tag) => _logSearchFilterBar?.AddTagFilter(tag);

  public void SetLogPageActive(bool active, LogPageActivationReason reason = LogPageActivationReason.Navigation)
  {
    if (!active)
    {
      _logTimer?.Stop();
      _pendingFollowTailInitialScroll = false;
      return;
    }

    EnsureLogViewInitialized();
    if (_logTimer is null)
      return;

    SyncFollowTailFromUi();
    _pendingFollowTailInitialScroll = _logController?.FollowTail == true;
    _logTimer.Start();
    EnsureLogScrollViewerHooked();

    if (reason == LogPageActivationReason.WindowRestored && _lastDisplayedLines.Count > 0)
    {
      ApplyFollowTailScrollIfEnabled();
      return;
    }

    RefreshLogView(forceRedraw: true);
    ApplyFollowTailScrollIfEnabled();
  }

  internal void EnsureLogScrollViewerHooked()
  {
    if (_lstLogView is null || _logScrollHooked)
      return;

    _logListPresenter?.EnsureScrollViewerResolved();
    _logScrollViewer = _logListPresenter?.ScrollViewer ?? LogViewListPresenter.FindScrollViewer(_lstLogView);
    if (_logScrollViewer is null)
      return;

    ScrollBarAutoHide.Attach(_logScrollViewer);
    _logScrollViewer.ScrollChanged += (_, _) =>
    {
      if (_logController is null || _logScrollViewer is null)
        return;
      if (_suppressFollowTailAutoSync || _pendingFollowTailInitialScroll)
        return;
      _logController.FollowTail = LogViewScrollState.IsAtTail(_logScrollViewer);
      SyncFollowTailToggle();
    };
    _logScrollHooked = true;
  }

  internal void RefreshLogViewForThemeChange() => RefreshLogView(forceRedraw: true);

  internal void Dispose()
  {
    _logTimer?.Stop();
    _logSearchDebounceTimer?.Stop();
    _logCustomRangeDebounceTimer?.Stop();
  }

  private void EnsureLogViewInitialized()
  {
    if (_logViewInitialized || _logPath is null)
      return;

    _logViewInitialized = true;
    var fallbackLogPath = _fallbackLogPath;
    var logController = new LogViewController(_logPath, fallbackLogPath);
    _logController = logController;
    _logSearchFilterBar = new LogSearchFilterBar();
    var logSearchFilterHost = Require<ContentControl>(_window, "logSearchFilterHost");
    logSearchFilterHost.Content = _logSearchFilterBar;
    _logTagFilterLinksPanel = Require<WrapPanel>(_window, "logTagFilterLinksPanel");
    foreach (var tag in LogTagFilterCatalog.SelectableTags)
    {
      _logTagFilterLinksPanel.Children.Add(
        LogTagFilterLinkFactory.CreateClickableTag(tag, AddLogTagFilter));
    }

    _lstLogView = Require<ListBox>(_window, "lstLogView");
    _logListPresenter = new LogViewListPresenter();
    _logListPresenter.Attach(_lstLogView);
    _lblLogStatus = Require<TextBlock>(_window, "lblLogStatus");
    _lstLogView.Loaded += (_, _) =>
    {
      EnsureLogScrollViewerHooked();
      ApplyFollowTailScrollIfEnabled();
    };
    _chkLogFollowTail = _window.FindName("chkLogFollowTail") as CheckBox;
    _cmbLogTimeRange = _window.FindName("cmbLogTimeRange") as ComboBox;
    _panelLogCustomRange = _window.FindName("panelLogCustomRange") as UIElement;
    _txtLogRangeStart = _window.FindName("txtLogRangeStart") as TextBox;
    _txtLogRangeEnd = _window.FindName("txtLogRangeEnd") as TextBox;
    _chkLogSearchCaseSensitive = _window.FindName("chkLogSearchCaseSensitive") as CheckBox;

    SyncCustomRangePanelVisibility();

    var btnLogCopy = _window.FindName("btnLogCopy") as Button;
    var btnLogExport = _window.FindName("btnLogExport") as Button;
    var btnLogOpenFolder = _window.FindName("btnLogOpenFolder") as Button;
    var btnLogScrollTop = _window.FindName("btnLogScrollTop") as Button;
    var btnLogScrollBottom = _window.FindName("btnLogScrollBottom") as Button;
    var btnLogRefresh = _window.FindName("btnLogRefresh") as Button;

    if (btnLogCopy is not null) btnLogCopy.Click += (_, _) => CopyVisibleLog();
    if (btnLogExport is not null) btnLogExport.Click += (_, _) => ExportVisibleLog();
    if (btnLogOpenFolder is not null) btnLogOpenFolder.Click += (_, _) => OpenLogFolder();
    if (btnLogScrollTop is not null) btnLogScrollTop.Click += (_, _) => ScrollLogToTop();
    if (btnLogScrollBottom is not null) btnLogScrollBottom.Click += (_, _) => ScrollLogToBottom();
    if (btnLogRefresh is not null) btnLogRefresh.Click += (_, _) => ForceRefreshLogView();
    if (_chkLogFollowTail is not null)
    {
      _chkLogFollowTail.Checked += (_, _) => SetFollowTail(true);
      _chkLogFollowTail.Unchecked += (_, _) => SetFollowTail(false);
    }

    _logSearchFilterBar.FiltersChanged += (_, _) => QueueLogSearchRefresh();
    if (_cmbLogTimeRange is not null)
    {
      _cmbLogTimeRange.SelectionChanged += (_, _) =>
      {
        SyncCustomRangePanelVisibility();
        RefreshLogView();
      };
    }

    if (_txtLogRangeStart is not null)
      _txtLogRangeStart.TextChanged += (_, _) => QueueLogCustomRangeRefresh();
    if (_txtLogRangeEnd is not null)
      _txtLogRangeEnd.TextChanged += (_, _) => QueueLogCustomRangeRefresh();
    if (_chkLogSearchCaseSensitive is not null)
    {
      _chkLogSearchCaseSensitive.Checked += (_, _) => RefreshLogView();
      _chkLogSearchCaseSensitive.Unchecked += (_, _) => RefreshLogView();
    }

    SyncFollowTailFromUi();

    var logTimer = new System.Windows.Threading.DispatcherTimer(
      System.Windows.Threading.DispatcherPriority.Background,
      _window.Dispatcher)
    {
      Interval = TimeSpan.FromSeconds(2),
    };
    logTimer.Tick += (_, _) => RefreshLogView();
    _logTimer = logTimer;
  }

  internal void RefreshLogView(bool forceRedraw = false)
  {
    if (_logController is null || _logListPresenter is null || _lblLogStatus is null) return;

    if (forceRedraw)
      ForceRefreshLogViewCountForTests++;

    var idleRead = LogViewIdleReader.TryRead(_root);
    if (idleRead.Seconds is not null)
      _logIdleSeconds = idleRead.Seconds;
    _logStatusMayBeStale = idleRead.StatusMayBeStale;

    var snapshot = BuildLogSnapshot();
    if (snapshot is null) return;

    var statusText = LogViewStatusTextBuilder.Build(snapshot, DateTime.Now, _logIdleSeconds, _logStatusMayBeStale);
    var plan = LogViewUpdatePlanner.CreatePlan(_lastDisplayedLines, snapshot.DisplayLines, forceRedraw);

    if (!forceRedraw && !snapshot.ContentChanged && plan.Mode == LogViewUpdateMode.NoChange)
    {
      _lblLogStatus.Text = statusText;
      return;
    }

    var scrollViewer = ResolveLogScrollViewer();
    var savedOffset = scrollViewer?.VerticalOffset ?? 0;
    var wasAtTail = scrollViewer is null || LogViewScrollState.IsAtTail(scrollViewer);
    var scrollToTail = _logController.FollowTail && wasAtTail;

    _logListPresenter.Apply(plan);
    _lastDisplayedLines = snapshot.DisplayLines.ToArray();
    _lblLogStatus.Text = statusText;

    if (scrollToTail)
    {
      ApplyFollowTailScrollIfEnabled();
      return;
    }

    if (scrollViewer is null)
      return;

    scrollViewer.UpdateLayout();
    if (!wasAtTail)
      scrollViewer.ScrollToVerticalOffset(savedOffset);
  }

  private ScrollViewer? ResolveLogScrollViewer()
  {
    if (_logScrollViewer is not null)
      return _logScrollViewer;

    if (_lstLogView is null)
      return null;

    _logListPresenter?.EnsureScrollViewerResolved();
    _logScrollViewer = _logListPresenter?.ScrollViewer ?? LogViewListPresenter.FindScrollViewer(_lstLogView);
    return _logScrollViewer;
  }

  private LogViewSnapshot? BuildLogSnapshot()
  {
    if (_logController is null) return null;

    _logController.SearchKeyword = _logSearchFilterBar?.Keyword ?? string.Empty;
    _logController.ActiveTagFilters = _logSearchFilterBar?.ActiveTags ?? [];
    _logController.SearchCaseSensitive = _chkLogSearchCaseSensitive?.IsChecked == true;
    _logController.TimeRange = ReadTimeRange(_cmbLogTimeRange);
    _logController.CustomRangeStart = TryReadCustomRangeStart();
    _logController.CustomRangeEnd = TryReadCustomRangeEnd();
    _logController.RefreshFromDisk();
    return _logController.GetSnapshot();
  }

  private void SyncCustomRangePanelVisibility()
  {
    if (_panelLogCustomRange is null || _cmbLogTimeRange is null)
      return;

    _panelLogCustomRange.Visibility = _cmbLogTimeRange.SelectedIndex == 3
      ? Visibility.Visible
      : Visibility.Collapsed;
  }

  private static LogViewTimeRange ReadTimeRange(ComboBox? comboBox)
  {
    return comboBox?.SelectedIndex switch
    {
      1 => LogViewTimeRange.Today,
      2 => LogViewTimeRange.LastHour,
      3 => LogViewTimeRange.Custom,
      _ => LogViewTimeRange.All,
    };
  }

  private DateTime? TryReadCustomRangeStart()
  {
    return LogViewCustomRangeParser.TryParse(_txtLogRangeStart?.Text, out var value)
      ? value
      : null;
  }

  private DateTime? TryReadCustomRangeEnd()
  {
    return LogViewCustomRangeParser.TryParse(_txtLogRangeEnd?.Text, out var value)
      ? value
      : null;
  }

  internal void ForceRefreshLogView()
  {
    _logController?.ForceRefresh();
    _lastDisplayedLines = Array.Empty<string>();
    RefreshLogView(forceRedraw: true);
  }

  private void CopyVisibleLog()
  {
    var snapshot = BuildLogSnapshot();
    if (snapshot is null) return;

    try
    {
      Clipboard.SetText(LogViewToolbarActions.BuildVisibleText(snapshot));
      _toastService.Show("已复制到剪贴板", isError: false);
    }
    catch (Exception ex)
    {
      _toastService.Show($"复制失败：{ex.Message}", isError: true);
    }
  }

  private void ExportVisibleLog()
  {
    var snapshot = BuildLogSnapshot();
    if (snapshot is null) return;

    var dialog = new Microsoft.Win32.SaveFileDialog
    {
      Filter = "文本文件 (*.txt)|*.txt",
      FileName = "SmartGuard.log.txt",
      DefaultExt = ".txt",
    };

    if (dialog.ShowDialog(_window) != true)
      return;

    ExportVisibleLogToPath(dialog.FileName);
  }

  internal void ExportVisibleLogToPath(string destinationPath)
  {
    var snapshot = BuildLogSnapshot();
    if (snapshot is null) return;

    try
    {
      LogViewToolbarActions.ExportVisibleText(
        LogViewToolbarActions.BuildVisibleText(snapshot),
        destinationPath);
      _toastService.Show("日志已导出", isError: false);
    }
    catch (Exception ex)
    {
      _toastService.Show($"导出失败：{ex.Message}", isError: true);
    }
  }

  private void OpenLogFolder()
  {
    if (string.IsNullOrWhiteSpace(_logPath)) return;

    var logFilePath = LogViewToolbarActions.ResolveLogFilePath(_logPath, _fallbackLogPath);
    try
    {
      System.Diagnostics.Process.Start(LogViewToolbarActions.CreateRevealLogFileProcessStartInfo(logFilePath));
    }
    catch (Exception ex)
    {
      _toastService.Show($"打开目录失败：{ex.Message}", isError: true);
    }
  }

  private void ScrollLogToTop()
  {
    _suppressFollowTailAutoSync = true;
    try
    {
      if (_logController is not null)
        _logController.FollowTail = false;
      SyncFollowTailToggle();
      ResolveLogScrollViewer()?.ScrollToVerticalOffset(0);
    }
    finally
    {
      ReleaseFollowTailAutoSyncSuppression();
    }
  }

  private void ScrollLogToBottom()
  {
    _suppressFollowTailAutoSync = true;
    try
    {
      ScrollLogViewToTail();
      if (_logController is not null && _chkLogFollowTail?.IsChecked != true)
        _logController.FollowTail = false;
      SyncFollowTailToggle();
    }
    finally
    {
      ReleaseFollowTailAutoSyncSuppression();
    }
  }

  private void ScrollLogViewToTail(bool deferred = false)
  {
    void ScrollNow()
    {
      var scrollViewer = ResolveLogScrollViewer();
      if (scrollViewer is null)
        return;

      scrollViewer.UpdateLayout();

      if (_lstLogView is not null && _lstLogView.Items.Count > 0)
        _lstLogView.ScrollIntoView(_lstLogView.Items[_lstLogView.Items.Count - 1]);

      scrollViewer.UpdateLayout();
      scrollViewer.ScrollToEnd();
    }

    ScrollNow();
    if (!deferred)
      return;

    _window.Dispatcher.BeginInvoke(
      ScrollNow,
      System.Windows.Threading.DispatcherPriority.Loaded);
  }

  private void ReleaseFollowTailAutoSyncSuppression()
  {
    _window.Dispatcher.BeginInvoke(
      () => _suppressFollowTailAutoSync = false,
      System.Windows.Threading.DispatcherPriority.ApplicationIdle);
  }

  private void SetFollowTail(bool enabled)
  {
    if (_logController is null) return;

    _logController.FollowTail = enabled;
    if (enabled)
    {
      RefreshLogView(forceRedraw: true);
      ApplyFollowTailScrollIfEnabled();
    }
  }

  private void SyncFollowTailToggle()
  {
    if (_logController is null || _chkLogFollowTail is null) return;

    _chkLogFollowTail.IsChecked = _logController.FollowTail;
  }

  private void SyncFollowTailFromUi()
  {
    if (_logController is null || _chkLogFollowTail is null)
      return;

    _logController.FollowTail = _chkLogFollowTail.IsChecked == true;
  }

  private void ApplyFollowTailScrollIfEnabled()
  {
    SyncFollowTailFromUi();
    if (_logController is null || !_logController.FollowTail)
    {
      _pendingFollowTailInitialScroll = false;
      return;
    }

    _pendingFollowTailInitialScroll = true;
    _suppressFollowTailAutoSync = true;
    ScrollLogViewToTail(deferred: true);
    _window.Dispatcher.BeginInvoke(
      () =>
      {
        ScrollLogViewToTail();
        _window.Dispatcher.BeginInvoke(
          () =>
          {
            if (_logScrollViewer is not null && LogViewScrollState.IsAtTail(_logScrollViewer))
              _pendingFollowTailInitialScroll = false;

            if (_logController is not null && _chkLogFollowTail?.IsChecked == true)
              _logController.FollowTail = true;

            SyncFollowTailToggle();
            ReleaseFollowTailAutoSyncSuppression();
          },
          System.Windows.Threading.DispatcherPriority.ApplicationIdle);
      },
      System.Windows.Threading.DispatcherPriority.Loaded);
  }

  private void QueueLogSearchRefresh()
  {
    if (_logSearchDebounceTimer is null)
    {
      _logSearchDebounceTimer = new System.Windows.Threading.DispatcherTimer(
        System.Windows.Threading.DispatcherPriority.Background,
        _window.Dispatcher)
      {
        Interval = TimeSpan.FromMilliseconds(300),
      };
      _logSearchDebounceTimer.Tick += (_, _) =>
      {
        _logSearchDebounceTimer.Stop();
        RefreshLogView();
      };
    }

    _logSearchDebounceTimer.Stop();
    _logSearchDebounceTimer.Start();
  }

  private void QueueLogCustomRangeRefresh()
  {
    if (_logCustomRangeDebounceTimer is null)
    {
      _logCustomRangeDebounceTimer = new System.Windows.Threading.DispatcherTimer(
        System.Windows.Threading.DispatcherPriority.Background,
        _window.Dispatcher)
      {
        Interval = TimeSpan.FromMilliseconds(300),
      };
      _logCustomRangeDebounceTimer.Tick += (_, _) =>
      {
        _logCustomRangeDebounceTimer.Stop();
        RefreshLogView();
      };
    }

    _logCustomRangeDebounceTimer.Stop();
    _logCustomRangeDebounceTimer.Start();
  }

  private static T Require<T>(Window window, string name) where T : class
  {
    return window.FindName(name) as T
      ?? throw new InvalidOperationException($"Missing control: {name}");
  }
}
