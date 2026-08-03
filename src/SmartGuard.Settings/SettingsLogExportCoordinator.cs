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
    string? fallbackLogPath)
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

  internal void ExportDiagnosticBundle(DiagnosticBundleScope scope)
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

    var request = BuildDiagnosticRequest(scope, controller);
    SettingsLogExportActions.ExportDiagnosticBundle(_window, _toastService, request, ExportDiagnosticToPath);
  }

  internal void ExportToPath(string destinationPath)
  {
    var snapshot = TryBuildSnapshot();
    if (snapshot is not null)
      SettingsLogExportActions.ExportToPath(snapshot, destinationPath, _toastService);
  }

  internal void ExportDiagnosticToPath(string destinationPath, DiagnosticBundleRequest request)
  {
    try
    {
      Task.Run(() => DiagnosticBundleExporter.ExportAsync(request with { DestinationZipPath = destinationPath })).GetAwaiter().GetResult();
      _toastService.Show("诊断包已保存", isError: false);
    }
    catch (Exception ex)
    {
      _toastService.Show($"导出诊断包失败：{ex.Message}", isError: true);
    }
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
      controller.NowProvider);

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
        ? controller.WindowEventsForDiagnostics
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
