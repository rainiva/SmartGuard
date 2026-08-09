using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings;

internal sealed partial class SettingsLogPageHost
{
  internal const string LoadingStatusText = "正在加载日志…";
  internal const string LoadFailedStatusText = "日志加载失败";

  private readonly Window _window;
  private readonly string _root;
  private readonly ToastNotificationService _toastService;
  private readonly SettingsLogSearchCoordinator _searchCoordinator;
  private readonly LogViewportController _logViewport;
  private readonly SettingsLogRefreshPipeline _refreshPipeline;
  private LogViewController? _logController;
  private readonly LogTablePresenter _logTablePresenter = new();
  private System.Windows.Threading.DispatcherTimer? _logTimer;
  private LogSearchFilterBar? _logSearchFilterBar;
  private ListBox? _lstLogView;
  private LogViewListPresenter? _logListPresenter;
  private SettingsLogExportCoordinator? _logExportCoordinator;
  private IReadOnlyList<string> _lastDisplayedLines = Array.Empty<string>();
  private int? _logIdleSeconds;
  private bool _logStatusMayBeStale;
  private bool _pagedHasActiveFilters;
  private TextBlock? _lblLogStatus;
  private Border? _logTopLoadingIndicator;
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
  internal LogTablePresenter TablePresenterForTests => _logTablePresenter;
  internal static int ForceRefreshLogViewCountForTests { get; private set; }

  internal static void ResetTestMetricsForTests() => ForceRefreshLogViewCountForTests = 0;

  // Control wiring uses SettingsLogPageInitializer via EnsureLogViewInitialized (Refresh partial).

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
    _searchCoordinator = new SettingsLogSearchCoordinator(window, () => OnFilterRefresh());
    _logViewport = new LogViewportController(
      window,
      () => _lstLogView,
      () => _logListPresenter,
      () => _chkLogFollowTail,
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
          LogStatusLiveRegion.Set(_lblLogStatus, $"{LoadFailedStatusText}：{failure.Message}");
      },
      ApplyRefreshResult,
      () => _logViewport.ApplyFollowLatestIfEnabled(fromRefreshPipeline: true),
      SetTopLoadingIndicator);
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
      _logViewport.ClearPendingInitialTailScroll();
      return;
    }

    _logPageActive = true;
    EnsureLogViewInitialized();
    if (_logTimer is null)
      return;

    _logViewport.SyncFollowLatestFromUi();
    if (_logController is not null)
      _logController.FollowTail = _logViewport.FollowLatest;
    if (_logViewport.FollowLatest)
      _logViewport.MarkPendingInitialTailScroll();
    else
      _logViewport.ClearPendingInitialTailScroll();
    EnsureLogScrollViewerHooked();

    if (reason == LogPageActivationReason.WindowRestored && _lastDisplayedLines.Count > 0)
    {
      EnsureLogAutoRefreshStarted();
      _logViewport.ApplyFollowLatestIfEnabled();
      return;
    }

    var isFirstVisit = _lastDisplayedLines.Count == 0;
    if (!isFirstVisit)
      EnsureLogAutoRefreshStarted();

    if (isFirstVisit && _lblLogStatus is not null)
      LogStatusLiveRegion.Set(_lblLogStatus, LoadingStatusText);

    if (IsPagedLogSource())
      RequestPagedQuery(captureNewSnapshot: isFirstVisit);
    else
      ScheduleLogRefresh(LogViewRefreshKind.Latest, forceRedraw: isFirstVisit, forceRefresh: isFirstVisit);
    if (!isFirstVisit)
      _logViewport.ApplyFollowLatestIfEnabled();
  }

  internal void EnsureLogAutoRefreshStarted()
  {
    if (_disposed || !_logPageActive || _logTimer is null || _logTimer.IsEnabled)
      return;

    _logTimer.Start();
  }

  internal void RefreshLogViewForThemeChange()
  {
    if (_disposed)
      return;

    OnFilterRefresh(forceRedraw: true);
  }

  internal void EnsureLogScrollViewerHooked()
  {
    if (_disposed)
      return;

    _logScrollViewer = _logViewport.EnsureHooked(_lstLogView, _logListPresenter);
    _logViewport.SetNearTopHandler(() =>
    {
      if (_disposed || !_logPageActive)
        return;

      ScheduleLogRefresh(LogViewRefreshKind.Older);
    });
  }

  internal void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;
    _logPageActive = false;
    _sessionMetadataCancellation?.Cancel();
    _sessionMetadataCancellation?.Dispose();
    _sessionMetadataCancellation = null;
    _sessionQueryCancellation?.Cancel();
    _sessionQueryCancellation?.Dispose();
    _sessionQueryCancellation = null;
    _correlationQueryCancellation?.Cancel();
    _correlationQueryCancellation?.Dispose();
    _correlationQueryCancellation = null;
    _statisticsCancellation?.Cancel();
    _statisticsCancellation?.Dispose();
    _statisticsCancellation = null;
    _refreshPipeline.Shutdown();
    _logTimer?.Stop();
    _searchCoordinator.Dispose();
  }

  internal void ExportVisibleLogToPath(string destinationPath)
  {
    if (_disposed)
      return;

    _logExportCoordinator?.ExportToPath(destinationPath);
  }

  internal ILogEventSource GetDiagnosticEventSource()
  {
    EnsureLogViewInitialized();
    return _logController?.EventSourceForCorrelation
      ?? new LogTailReader(_logPath!, _fallbackLogPath);
  }

  internal LogSourceCatalog? GetDiagnosticCatalogSnapshot()
    => _logController?.CatalogSnapshotForDiagnostics;
}
