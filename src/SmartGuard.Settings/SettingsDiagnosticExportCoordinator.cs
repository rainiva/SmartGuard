using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings;

internal sealed class SettingsDiagnosticExportCoordinator
{
  private readonly string _root;
  private readonly Func<string> _getAppVersion;
  private readonly Action<string, bool> _showToast;
  private readonly Func<(string LogPath, string StartupPath)> _getDiagnosticPaths;
  private readonly SettingsDiagnosticExportSession _session;
  private readonly Func<DiagnosticBundleRequest, CancellationToken, Task> _export;
  private readonly Func<Window, string?> _pickDestinationPath;
  private readonly Func<bool> _isAlive;

  internal SettingsDiagnosticExportCoordinator(
    string root,
    Func<string> getAppVersion,
    Action<string, bool> showToast,
    Func<(string LogPath, string StartupPath)> getDiagnosticPaths,
    SettingsDiagnosticExportSession session,
    Func<Window, string?>? pickDestinationPath = null,
    Func<DiagnosticBundleRequest, CancellationToken, Task>? export = null,
    Func<bool>? isAlive = null)
  {
    _root = root;
    _getAppVersion = getAppVersion;
    _showToast = showToast;
    _getDiagnosticPaths = getDiagnosticPaths;
    _session = session;
    _export = export ?? ((request, token) => DiagnosticBundleExporter.ExportAsync(request, cancellationToken: token));
    _pickDestinationPath = pickDestinationPath ?? PickDestinationPathDefault;
    _isAlive = isAlive ?? (() => true);
  }

  internal void WireAboutPage(Window window, Button? button = null)
  {
    button ??= window.FindName("btnExportDiagnostic") as Button;
    if (button is null)
      return;

    button.Click += async (_, _) => await ExportViaDialog(window);
  }

  internal Task ExportViaDialog(Window window)
    => RunExport(() => _pickDestinationPath(window), overwriteExisting: true);

  internal Task ExportToPath(string destinationPath, bool overwriteExisting = false)
    => RunExport(() => destinationPath, overwriteExisting);

  private async Task RunExport(Func<string?> pickPath, bool overwriteExisting)
  {
    try
    {
      await _session.RunAsync(DiagnosticExportOrigin.About, async token =>
      {
        var path = pickPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        var paths = _getDiagnosticPaths();
        var version = _getAppVersion();
        await Task.Run(async () =>
        {
          token.ThrowIfCancellationRequested();
          var source = new LogTailReader(paths.LogPath, paths.StartupPath);
          var request = new DiagnosticBundleRequest
          {
            Root = _root, DestinationZipPath = path, AppVersion = version,
            EventSource = source, CatalogSnapshot = source.Catalog,
            Scope = DiagnosticBundleScope.FullAvailableHistory, OverwriteExisting = overwriteExisting,
            IncludeStartupLog = true, IncludeEmergencyLog = true, IncludeCrashLog = true, IncludeCrashRingLog = true,
          };
          await _export(request, token);
        }, token);
        if (CanApplyResult) _showToast("诊断包已保存", false);
      });
    }
    catch (OperationCanceledException) { if (CanApplyResult) _showToast("导出已取消", false); }
    catch (Exception ex)
    {
      if (CanApplyResult) _showToast($"导出诊断包失败：{ex.Message}", true);
    }
  }

  private bool CanApplyResult => _session.IsAlive && _isAlive();

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
