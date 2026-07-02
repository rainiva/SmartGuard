using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal sealed class SettingsDiagnosticExportCoordinator
{
  private readonly string _root;
  private readonly Func<string> _getAppVersion;
  private readonly Action<string, bool> _showToast;
  private readonly Func<Window, string?> _pickDestinationPath;

  internal SettingsDiagnosticExportCoordinator(
    string root,
    Func<string> getAppVersion,
    Action<string, bool> showToast,
    Func<Window, string?>? pickDestinationPath = null)
  {
    _root = root;
    _getAppVersion = getAppVersion;
    _showToast = showToast;
    _pickDestinationPath = pickDestinationPath ?? PickDestinationPathDefault;
  }

  internal void WireAboutPage(Window window, Button? button = null)
  {
    button ??= window.FindName("btnExportDiagnostic") as Button;
    if (button is null)
      return;

    button.Click += (_, _) => ExportViaDialog(window);
  }

  internal void ExportViaDialog(Window window)
  {
    var path = _pickDestinationPath(window);
    if (string.IsNullOrWhiteSpace(path))
      return;

    ExportToPath(path);
  }

  internal void ExportToPath(string destinationPath)
  {
    try
    {
      DiagnosticBundleExporter.Export(_root, destinationPath, _getAppVersion());
      _showToast("诊断包已保存", false);
    }
    catch (Exception ex)
    {
      _showToast($"导出诊断包失败：{ex.Message}", true);
    }
  }

  private static string? PickDestinationPathDefault(Window window)
  {
    var dialog = new Microsoft.Win32.SaveFileDialog
    {
      Filter = "ZIP 压缩文件 (*.zip)|*.zip",
      FileName = $"SmartGuard-diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
      DefaultExt = ".zip",
    };

    return dialog.ShowDialog(window) == true ? dialog.FileName : null;
  }
}
