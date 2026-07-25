using System.Windows;
using System.Windows.Controls;

namespace SmartGuard.Settings;

internal sealed class SettingsLogExportCoordinator
{
  private const string StillLoadingToastText = "日志仍在加载，请稍后再试";

  private readonly Window _window;
  private readonly ToastNotificationService _toastService;
  private readonly Func<bool> _isRefreshInFlight;
  private readonly Func<LogViewController?> _getLogController;
  private readonly Func<LogSearchFilterBar?> _getSearchFilterBar;
  private readonly Func<CheckBox?> _getCaseSensitiveCheckBox;
  private readonly Func<ComboBox?> _getTimeRangeComboBox;
  private readonly Func<TextBox?> _getRangeStartTextBox;
  private readonly Func<TextBox?> _getRangeEndTextBox;
  private readonly string? _logPath;
  private readonly string? _fallbackLogPath;

  internal SettingsLogExportCoordinator(
    Window window,
    ToastNotificationService toastService,
    Func<bool> isRefreshInFlight,
    Func<LogViewController?> getLogController,
    Func<LogSearchFilterBar?> getSearchFilterBar,
    Func<CheckBox?> getCaseSensitiveCheckBox,
    Func<ComboBox?> getTimeRangeComboBox,
    Func<TextBox?> getRangeStartTextBox,
    Func<TextBox?> getRangeEndTextBox,
    string? logPath,
    string? fallbackLogPath)
  {
    _window = window;
    _toastService = toastService;
    _isRefreshInFlight = isRefreshInFlight;
    _getLogController = getLogController;
    _getSearchFilterBar = getSearchFilterBar;
    _getCaseSensitiveCheckBox = getCaseSensitiveCheckBox;
    _getTimeRangeComboBox = getTimeRangeComboBox;
    _getRangeStartTextBox = getRangeStartTextBox;
    _getRangeEndTextBox = getRangeEndTextBox;
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

  internal void ExportToPath(string destinationPath)
  {
    var snapshot = TryBuildSnapshot();
    if (snapshot is not null)
      SettingsLogExportActions.ExportToPath(snapshot, destinationPath, _toastService);
  }

  internal void OpenFolder()
    => SettingsLogExportActions.OpenLogFolder(_logPath, _fallbackLogPath, _toastService);

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
