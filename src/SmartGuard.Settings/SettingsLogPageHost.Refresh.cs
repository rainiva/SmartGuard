using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal sealed partial class SettingsLogPageHost
{
  private SettingsLogPaginationCoordinator? _logPaginationCoordinator;

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
      AddLogTagFilter,
      () =>
      {
        EnsureLogScrollViewerHooked();
        _logViewport.ApplyFollowLatestIfEnabled();
      },
      () => ScheduleLogRefresh(LogViewRefreshKind.Newer));
    _logController = controls.Controller;
    _logPaginationCoordinator ??= new SettingsLogPaginationCoordinator(
      (intent, execute) => _refreshPipeline.SchedulePaged(intent, execute),
      ExecutePagedQueryAsync,
      () => _disposed);
    _refreshPipeline.ConfigurePagedApply(_logPaginationCoordinator.ApplyModel);
    _searchCoordinator.QueryGenerationChanged += OnLogQueryGenerationChanged;
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
      ForceRefreshLogViewFromToolbar);
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
    WireStatisticsUi();
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
    ScheduleStatisticsRefresh();
  }

  private void OnLogQueryGenerationChanged(int _)
    => _logPaginationCoordinator?.MarkSnapshotStale();

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
