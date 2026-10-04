using System.IO;
using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings;

internal sealed class SettingsLogExportCoordinator
{
  private const string StillLoadingToastText = "日志仍在加载，请稍后再试";

  private readonly Window _window;
  private readonly string _root;
  private readonly ToastNotificationService _toastService;
  private readonly Func<bool> _isRefreshInFlight;
  private readonly Func<LogViewController?> _getLogController;
  private readonly Func<LogSearchFilterBar?> _getSearchFilterBar;
  private readonly Func<CheckBox?> _getCaseSensitiveCheckBox;
  private readonly Func<ComboBox?> _getTimeRangeComboBox;
  private readonly Func<TextBox?> _getRangeStartTextBox;
  private readonly Func<TextBox?> _getRangeEndTextBox;
  private readonly Func<LogCorrelationCoordinator> _getCorrelationCoordinator;
  private readonly string? _logPath;
  private readonly string? _fallbackLogPath;
  private readonly Func<LogTablePresenter?> _getTablePresenter;
  private readonly Func<LogQueryPageIndex?> _getPageIndex;
  private readonly Func<ILogEventSource?> _getEventSource;
  private readonly Func<LogSortDirection> _getSortDirection;
  private readonly Func<int> _getPageSize;
  private readonly SettingsDiagnosticExportSession _diagnosticSession;
  private readonly Func<bool> _isAlive;
  private readonly Func<DiagnosticBundleRequest, CancellationToken, Task> _exportDiagnostic;

  internal SettingsLogExportCoordinator(
    Window window,
    string root,
    ToastNotificationService toastService,
    Func<bool> isRefreshInFlight,
    Func<LogViewController?> getLogController,
    Func<LogSearchFilterBar?> getSearchFilterBar,
    Func<CheckBox?> getCaseSensitiveCheckBox,
    Func<ComboBox?> getTimeRangeComboBox,
    Func<TextBox?> getRangeStartTextBox,
    Func<TextBox?> getRangeEndTextBox,
    Func<LogCorrelationCoordinator> getCorrelationCoordinator,
    string? logPath,
    string? fallbackLogPath,
    SettingsDiagnosticExportSession diagnosticSession,
    Func<LogTablePresenter?>? getTablePresenter = null,
    Func<LogQueryPageIndex?>? getPageIndex = null,
    Func<ILogEventSource?>? getEventSource = null,
    Func<LogSortDirection>? getSortDirection = null,
    Func<int>? getPageSize = null,
    Func<bool>? isAlive = null,
    Func<DiagnosticBundleRequest, CancellationToken, Task>? exportDiagnostic = null)
  {
    _window = window;
    _root = root;
    _toastService = toastService;
    _isRefreshInFlight = isRefreshInFlight;
    _getLogController = getLogController;
    _getSearchFilterBar = getSearchFilterBar;
    _getCaseSensitiveCheckBox = getCaseSensitiveCheckBox;
    _getTimeRangeComboBox = getTimeRangeComboBox;
    _getRangeStartTextBox = getRangeStartTextBox;
    _getRangeEndTextBox = getRangeEndTextBox;
    _getCorrelationCoordinator = getCorrelationCoordinator;
    _logPath = logPath;
    _fallbackLogPath = fallbackLogPath;
    _getTablePresenter = getTablePresenter ?? (() => null);
    _getPageIndex = getPageIndex ?? (() => null);
    _getEventSource = getEventSource ?? (() => null);
    _getSortDirection = getSortDirection ?? (() => LogSortDirection.NewestFirst);
    _getPageSize = getPageSize ?? (() => 20);
    _diagnosticSession = diagnosticSession;
    _isAlive = isAlive ?? (() => true);
    _exportDiagnostic = exportDiagnostic ?? ((request, token) => DiagnosticBundleExporter.ExportAsync(request, cancellationToken: token));
  }

  internal void CopyVisible()
  {
    var snapshot = TryBuildSnapshot();
    if (snapshot is not null)
      SettingsLogExportActions.CopyVisible(snapshot, _toastService);
  }

  internal void ExportVisible()
  {
    var snapshot = TryBuildSnapshot();
    if (snapshot is not null)
      SettingsLogExportActions.ExportVisible(snapshot, _window, _toastService, ExportToPath);
  }

  internal void CopyCurrentPage()
  {
    var rows = _getTablePresenter()?.Rows;
    if (rows is not null)
      SettingsLogExportActions.CopyCurrentPage(rows, _toastService);
  }

  internal void ExportCurrentPage()
  {
    var rows = _getTablePresenter()?.Rows;
    if (rows is not null)
      SettingsLogExportActions.ExportCurrentPage(rows, _window, _toastService, ExportCurrentPageToPath);
  }

  internal void ExportAllMatches()
  {
    if (_isRefreshInFlight())
    {
      _toastService.Show(StillLoadingToastText, isError: false);
      return;
    }

    if (_getPageIndex() is null || _getEventSource() is null)
    {
      _toastService.Show("当前日志查询尚未完成", isError: false);
      return;
    }

    var dialog = new Microsoft.Win32.SaveFileDialog
    {
      Filter = "文本文件 (*.txt)|*.txt",
      FileName = "SmartGuard.log.all.txt",
      DefaultExt = ".txt",
    };
    if (dialog.ShowDialog(_window) == true)
      _ = ExportAllMatchesToPathAsync(dialog.FileName);
  }

  internal void ExportCurrentPageToPath(string destinationPath)
  {
    try
    {
      var rows = _getTablePresenter()?.Rows
        ?? throw new InvalidOperationException("当前页尚未加载。");
      LogViewToolbarActions.ExportVisibleText(
        LogViewToolbarActions.BuildCurrentPageText(rows),
        destinationPath);
      _toastService.Show("当前页日志已导出", isError: false);
    }
    catch (Exception ex)
    {
      _toastService.Show($"导出失败：{ex.Message}", isError: true);
    }
  }

  internal async Task ExportAllMatchesToPathAsync(string destinationPath)
  {
    try
    {
      var eventSource = _getEventSource()
        ?? throw new InvalidOperationException("日志源尚未初始化。");
      var pageIndex = _getPageIndex()
        ?? throw new InvalidOperationException("当前日志查询尚未完成。");

      await using var writer = new StreamWriter(
        destinationPath,
        append: false,
        new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
      var service = new LogQueryService();
      await foreach (var logEvent in service.StreamMatchingEventsAsync(
                       eventSource,
                       pageIndex,
                       _getSortDirection(),
                       _getPageSize(),
                       CancellationToken.None))
      {
        await writer.WriteLineAsync(LogViewEventFilter.GetDisplayLine(logEvent));
      }

      _toastService.Show("全部匹配日志已导出", isError: false);
    }
    catch (Exception ex)
    {
      _toastService.Show($"导出失败：{ex.Message}", isError: true);
    }
  }

  internal async Task ExportDiagnosticBundle(DiagnosticBundleScope scope)
  {
    if (_isRefreshInFlight())
    {
      _toastService.Show(StillLoadingToastText, isError: false);
      return;
    }

    var controller = _getLogController();
    if (controller is null)
      return;

    SettingsLogSearchCoordinator.ApplyFilters(
      controller,
      _getSearchFilterBar(),
      _getCaseSensitiveCheckBox(),
      _getTimeRangeComboBox(),
      _getRangeStartTextBox(),
      _getRangeEndTextBox());

    try
    {
      await _diagnosticSession.RunAsync(DiagnosticExportOrigin.Logs, async token =>
      {
        var request = BuildDiagnosticRequest(scope, controller);
        var dialog = new Microsoft.Win32.SaveFileDialog
        { Filter = "ZIP 压缩文件 (*.zip)|*.zip", FileName = $"SmartGuard-diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.zip", DefaultExt = ".zip" };
        if (dialog.ShowDialog(_window) != true) return;
        await ExportDiagnosticWorker(dialog.FileName, request with { OverwriteExisting = true }, token);
      });
    }
    catch (OperationCanceledException) { if (CanApplyDiagnosticResult) _toastService.Show("导出已取消", false); }
    catch (Exception ex) { if (CanApplyDiagnosticResult) _toastService.Show($"导出诊断包失败：{ex.Message}", true); }
  }

  internal void ExportToPath(string destinationPath)
  {
    var snapshot = TryBuildSnapshot();
    if (snapshot is not null)
      SettingsLogExportActions.ExportToPath(snapshot, destinationPath, _toastService);
  }

  internal async Task ExportDiagnosticToPath(string destinationPath, DiagnosticBundleRequest request)
  {
    try
    {
      await _diagnosticSession.RunAsync(DiagnosticExportOrigin.Logs, token => ExportDiagnosticWorker(destinationPath, request, token));
    }
    catch (OperationCanceledException) { if (CanApplyDiagnosticResult) _toastService.Show("导出已取消", false); }
    catch (Exception ex)
    {
      if (CanApplyDiagnosticResult) _toastService.Show($"导出诊断包失败：{ex.Message}", isError: true);
    }
  }

  private bool CanApplyDiagnosticResult => _diagnosticSession.IsAlive && _isAlive();

  private async Task ExportDiagnosticWorker(string destination, DiagnosticBundleRequest request, CancellationToken token)
  {
    // Freeze the bounded UI window before scheduling. The reader used by
    // refresh has mutable scan state and must never be shared with this flight.
    var recent = request.RecentWindowEvents?.ToArray();
    var logPath = _logPath ?? SmartGuardPaths.DefaultLogFile(_root);
    var startupPath = _fallbackLogPath ?? SmartGuardPaths.StartupLogFile(_root);
    await Task.Run(async () =>
    {
      token.ThrowIfCancellationRequested();
      var source = new LogTailReader(logPath, startupPath);
      await _exportDiagnostic(request with { DestinationZipPath = destination, EventSource = source, CatalogSnapshot = source.Catalog, RecentWindowEvents = recent }, token);
    }, token);
    if (CanApplyDiagnosticResult) _toastService.Show("诊断包已保存", false);
  }

  internal void OpenFolder()
    => SettingsLogExportActions.OpenLogFolder(_logPath, _fallbackLogPath, _toastService);

  private DiagnosticBundleRequest BuildDiagnosticRequest(DiagnosticBundleScope scope, LogViewController controller)
  {
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
      controller.NowProvider,
      controller.ComponentFilter);

    var correlation = _getCorrelationCoordinator();
    var effectiveScope = correlation.IsActive ? DiagnosticBundleScope.CurrentOperation : scope;
    var operationKey = correlation.IsActive ? correlation.ActiveKey : null;
    var filterExpression = correlation.IsActive
      ? correlation.BuildActiveQuery()
      : filterState.QueryExpression;

    return new DiagnosticBundleRequest
    {
      Root = _root,
      DestinationZipPath = string.Empty,
      EventSource = controller.EventSourceForCorrelation,
      Scope = effectiveScope,
      FilterExpression = filterExpression,
      OperationKey = operationKey,
      RecentWindowEvents = effectiveScope == DiagnosticBundleScope.RecentWindow
        ? controller.WindowEventsForDiagnostics.ToArray()
        : null,
      CatalogSnapshot = controller.CatalogSnapshotForDiagnostics,
      IncludeStartupLog = true,
    };
  }

  private LogViewSnapshot? TryBuildSnapshot()
  {
    if (_isRefreshInFlight())
    {
      _toastService.Show(StillLoadingToastText, isError: false);
      return null;
    }

    var controller = _getLogController();
    if (controller is null)
      return null;

    SettingsLogSearchCoordinator.ApplyFilters(
      controller,
      _getSearchFilterBar(),
      _getCaseSensitiveCheckBox(),
      _getTimeRangeComboBox(),
      _getRangeStartTextBox(),
      _getRangeEndTextBox());
    controller.RefreshFromDisk();
    return controller.GetSnapshot();
  }
}
