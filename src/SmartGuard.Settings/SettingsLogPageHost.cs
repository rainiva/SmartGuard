using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SmartGuard.Settings;

internal sealed class SettingsLogPageHost
{
  private readonly Window _window;
  private readonly string _root;
  private readonly ToastNotificationService _toastService;
  private readonly SettingsLogSearchCoordinator _searchCoordinator;
  private readonly SettingsLogFollowTailCoordinator _followTailCoordinator;

  private LogViewController? _logController;
  private System.Windows.Threading.DispatcherTimer? _logTimer;
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
    _searchCoordinator = new SettingsLogSearchCoordinator(window, () => RefreshLogView());
    _followTailCoordinator = new SettingsLogFollowTailCoordinator(
      window,
      () => _logController,
      () => _chkLogFollowTail,
      () => _lstLogView,
      ResolveLogScrollViewer,
      forceRedraw => RefreshLogView(forceRedraw));
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
      _followTailCoordinator.PendingFollowTailInitialScroll = false;
      return;
    }

    EnsureLogViewInitialized();
    if (_logTimer is null)
      return;

    _followTailCoordinator.SyncFollowTailFromUi();
    _followTailCoordinator.PendingFollowTailInitialScroll = _logController?.FollowTail == true;
    _logTimer.Start();
    EnsureLogScrollViewerHooked();

    if (reason == LogPageActivationReason.WindowRestored && _lastDisplayedLines.Count > 0)
    {
      _followTailCoordinator.ApplyScrollIfEnabled();
      return;
    }

    RefreshLogView(forceRedraw: true);
    _followTailCoordinator.ApplyScrollIfEnabled();
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
      if (_logScrollViewer is not null)
        _followTailCoordinator.OnScrollChanged(_logScrollViewer);
    };
    _logScrollHooked = true;
  }

  internal void RefreshLogViewForThemeChange() => RefreshLogView(forceRedraw: true);

  internal void Dispose()
  {
    _logTimer?.Stop();
    _searchCoordinator.Dispose();
  }

  private void EnsureLogViewInitialized()
  {
    if (_logViewInitialized || _logPath is null)
      return;

    _logViewInitialized = true;
    var logController = new LogViewController(_logPath, _fallbackLogPath);
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
      _followTailCoordinator.ApplyScrollIfEnabled();
    };
    _chkLogFollowTail = _window.FindName("chkLogFollowTail") as CheckBox;
    _cmbLogTimeRange = _window.FindName("cmbLogTimeRange") as ComboBox;
    _panelLogCustomRange = _window.FindName("panelLogCustomRange") as UIElement;
    _txtLogRangeStart = _window.FindName("txtLogRangeStart") as TextBox;
    _txtLogRangeEnd = _window.FindName("txtLogRangeEnd") as TextBox;
    _chkLogSearchCaseSensitive = _window.FindName("chkLogSearchCaseSensitive") as CheckBox;

    SettingsLogSearchCoordinator.SyncCustomRangePanelVisibility(_panelLogCustomRange, _cmbLogTimeRange);

    SettingsLogPageHostWiring.WireToolbarButtons(
      _window,
      _chkLogFollowTail,
      _followTailCoordinator,
      CopyVisibleLog,
      ExportVisibleLog,
      OpenLogFolder,
      ForceRefreshLogView);
    SettingsLogPageHostWiring.WireSearchAndFilterEvents(
      _logSearchFilterBar,
      _searchCoordinator,
      _cmbLogTimeRange,
      _panelLogCustomRange,
      _txtLogRangeStart,
      _txtLogRangeEnd,
      _chkLogSearchCaseSensitive,
      () => RefreshLogView());
    _followTailCoordinator.SyncFollowTailFromUi();

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

    var snapshot = BuildLogSnapshot();
    if (snapshot is null) return;

    var forceCount = ForceRefreshLogViewCountForTests;
    SettingsLogViewRefresher.Apply(
      snapshot,
      _logController,
      _logListPresenter,
      _lblLogStatus,
      _root,
      ref _logIdleSeconds,
      ref _logStatusMayBeStale,
      ref _lastDisplayedLines,
      ref forceCount,
      _followTailCoordinator,
      ResolveLogScrollViewer,
      forceRedraw);
    ForceRefreshLogViewCountForTests = forceCount;
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

    SettingsLogSearchCoordinator.ApplyFilters(
      _logController,
      _logSearchFilterBar,
      _chkLogSearchCaseSensitive,
      _cmbLogTimeRange,
      _txtLogRangeStart,
      _txtLogRangeEnd);
    _logController.RefreshFromDisk();
    return _logController.GetSnapshot();
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
    SettingsLogExportActions.CopyVisible(snapshot, _toastService);
  }

  private void ExportVisibleLog()
  {
    var snapshot = BuildLogSnapshot();
    if (snapshot is null) return;
    SettingsLogExportActions.ExportVisible(snapshot, _window, _toastService, ExportVisibleLogToPath);
  }

  internal void ExportVisibleLogToPath(string destinationPath)
  {
    var snapshot = BuildLogSnapshot();
    if (snapshot is null) return;
    SettingsLogExportActions.ExportToPath(snapshot, destinationPath, _toastService);
  }

  private void OpenLogFolder()
    => SettingsLogExportActions.OpenLogFolder(_logPath, _fallbackLogPath, _toastService);

  private static T Require<T>(Window window, string name) where T : class
    => window.FindName(name) as T
      ?? throw new InvalidOperationException($"Missing control: {name}");
}
