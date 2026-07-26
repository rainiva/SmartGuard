using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SmartGuard.Settings;

internal sealed class SettingsLogPageHost
{
  internal const string LoadingStatusText = "正在加载日志…";
  internal const string LoadFailedStatusText = "日志加载失败";

  private readonly Window _window;
  private readonly string _root;
  private readonly ToastNotificationService _toastService;
  private readonly SettingsLogSearchCoordinator _searchCoordinator;
  private readonly SettingsLogFollowTailCoordinator _followTailCoordinator;
  private readonly SettingsLogRefreshPipeline _refreshPipeline;
  private LogViewController? _logController;
  private System.Windows.Threading.DispatcherTimer? _logTimer;
  private LogSearchFilterBar? _logSearchFilterBar;
  private ListBox? _lstLogView;
  private LogViewListPresenter? _logListPresenter;
  private SettingsLogExportCoordinator? _logExportCoordinator;
  private IReadOnlyList<string> _lastDisplayedLines = Array.Empty<string>();
  private int? _logIdleSeconds;
  private bool _logStatusMayBeStale;
  private TextBlock? _lblLogStatus;
  private readonly SettingsLogScrollCoordinator _logScrollCoordinator = new();
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
  private bool _logPageActive;
  private volatile bool _disposed;

  internal bool IsLogViewInitializedForTests => _logViewInitialized;
  internal bool IsRefreshInFlightForTests => _refreshPipeline.IsRefreshInFlight;
  internal int ActiveRefreshCountForTests => _refreshPipeline.ActiveRefreshCount;
  internal int ApplyAfterDisposeCountForTests => _refreshPipeline.ApplyAfterDisposeCount;
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
      () => _logScrollCoordinator.Resolve(_lstLogView, _logListPresenter),
      forceRedraw => RefreshLogView(forceRedraw));
    _refreshPipeline = new SettingsLogRefreshPipeline(
      window,
      () => _disposed,
      () => _logPageActive,
      GetRefreshableController,
      controller => SettingsLogSearchCoordinator.ApplyFilters(
        controller,
        _logSearchFilterBar,
        _chkLogSearchCaseSensitive,
        _cmbLogTimeRange,
        _txtLogRangeStart,
        _txtLogRangeEnd),
      failure =>
      {
        if (_lblLogStatus is not null)
          _lblLogStatus.Text = $"{LoadFailedStatusText}：{failure.Message}";
      },
      ApplySnapshot,
      () => _followTailCoordinator.ApplyScrollIfEnabled());
  }

  private LogViewController? GetRefreshableController() =>
    _logListPresenter is null || _lblLogStatus is null ? null : _logController;

  internal void ConfigurePaths(string logPath, string? fallbackLogPath)
  {
    if (_disposed)
      return;

    _logPath = logPath;
    _fallbackLogPath = fallbackLogPath;
  }

  internal void AddLogTagFilter(string tag)
  {
    if (_disposed)
      return;

    _logSearchFilterBar?.AddTagFilter(tag);
  }

  public void SetLogPageActive(bool active, LogPageActivationReason reason = LogPageActivationReason.Navigation)
  {
    if (_disposed)
      return;

    if (!active)
    {
      _logPageActive = false;
      _logTimer?.Stop();
      _followTailCoordinator.PendingFollowTailInitialScroll = false;
      return;
    }

    _logPageActive = true;
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

    var isFirstVisit = _lastDisplayedLines.Count == 0;
    if (isFirstVisit && _lblLogStatus is not null)
      _lblLogStatus.Text = LoadingStatusText;

    // First visit: force full redraw once content arrives. Revisit: keep list, incremental apply.
    ScheduleLogRefresh(forceRefresh: false, forceRedraw: isFirstVisit);
    if (!isFirstVisit)
      _followTailCoordinator.ApplyScrollIfEnabled();
  }

  internal void RefreshLogViewForThemeChange()
  {
    if (_disposed)
      return;

    ScheduleLogRefresh(forceRefresh: false, forceRedraw: true);
  }

  internal void EnsureLogScrollViewerHooked()
  {
    if (_disposed)
      return;

    _logScrollViewer = _logScrollCoordinator.EnsureHooked(
      _lstLogView, _logListPresenter, _followTailCoordinator);
  }

  internal void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;
    _logPageActive = false;
    _refreshPipeline.Shutdown();
    _logTimer?.Stop();
    _searchCoordinator.Dispose();
  }

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
        _followTailCoordinator.ApplyScrollIfEnabled();
      },
      () => RefreshLogView());
    _logController = controls.Controller;
    _logSearchFilterBar = controls.SearchFilterBar;
    _lstLogView = controls.ListView;
    _logListPresenter = controls.ListPresenter;
    _lblLogStatus = controls.StatusLabel;
    _chkLogFollowTail = controls.FollowTailCheckBox;
    _cmbLogTimeRange = controls.TimeRangeComboBox;
    _panelLogCustomRange = controls.CustomRangePanel;
    _txtLogRangeStart = controls.RangeStartTextBox;
    _txtLogRangeEnd = controls.RangeEndTextBox;
    _chkLogSearchCaseSensitive = controls.CaseSensitiveCheckBox;
    _logTimer = controls.RefreshTimer;
    _logExportCoordinator = new SettingsLogExportCoordinator(
      _window,
      _toastService,
      () => _refreshPipeline.UiRefreshInFlight,
      () => _logController,
      () => _logSearchFilterBar,
      () => _chkLogSearchCaseSensitive,
      () => _cmbLogTimeRange,
      () => _txtLogRangeStart,
      () => _txtLogRangeEnd,
      _logPath,
      _fallbackLogPath);

    SettingsLogPageHostWiring.WireToolbarButtons(
      _window,
      _chkLogFollowTail,
      _followTailCoordinator,
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
      () => RefreshLogView());
    _followTailCoordinator.SyncFollowTailFromUi();
  }

  internal void RefreshLogView(bool forceRedraw = false)
  {
    if (_disposed)
      return;

    ScheduleLogRefresh(forceRefresh: false, forceRedraw: forceRedraw);
  }

  private void ScheduleLogRefresh(bool forceRefresh, bool forceRedraw) =>
    _refreshPipeline.Schedule(forceRefresh, forceRedraw);

  private void ApplySnapshot(LogViewSnapshot snapshot, bool forceRedraw)
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
      () => _logScrollCoordinator.Resolve(_lstLogView, _logListPresenter),
      _logScrollCoordinator.GetPreferredHorizontalOffset, forceRedraw);
    ForceRefreshLogViewCountForTests = forceCount;
  }

  internal void ForceRefreshLogView()
  {
    if (_disposed)
      return;

    _lastDisplayedLines = Array.Empty<string>();
    if (_lblLogStatus is not null)
      _lblLogStatus.Text = LoadingStatusText;
    ScheduleLogRefresh(forceRefresh: true, forceRedraw: true);
  }

  internal void ExportVisibleLogToPath(string destinationPath)
  {
    if (_disposed)
      return;

    _logExportCoordinator?.ExportToPath(destinationPath);
  }
}
