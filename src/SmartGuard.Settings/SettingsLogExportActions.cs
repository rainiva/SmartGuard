using System.IO;
using System.Windows;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal static class SettingsLogExportActions
{
  internal static void CopyCurrentPage(IReadOnlyList<LogTableRow> rows, ToastNotificationService toast)
  {
    try
    {
      Clipboard.SetText(LogViewToolbarActions.BuildCurrentPageText(rows));
      toast.Show("当前页已复制到剪贴板", isError: false);
    }
    catch (Exception ex)
    {
      toast.Show($"复制失败：{ex.Message}", isError: true);
    }
  }

  internal static void ExportCurrentPage(
    IReadOnlyList<LogTableRow> rows,
    Window window,
    ToastNotificationService toast,
    Action<string> exportToPath)
  {
    var dialog = new Microsoft.Win32.SaveFileDialog
    {
      Filter = "文本文件 (*.txt)|*.txt",
      FileName = "SmartGuard.log.page.txt",
      DefaultExt = ".txt",
    };

    if (dialog.ShowDialog(window) == true)
      exportToPath(dialog.FileName);
  }

  internal static void CopyVisible(LogViewSnapshot snapshot, ToastNotificationService toast)
  {
    try
    {
      Clipboard.SetText(LogViewToolbarActions.BuildVisibleText(snapshot));
      toast.Show("已复制到剪贴板", isError: false);
    }
    catch (Exception ex)
    {
      toast.Show($"复制失败：{ex.Message}", isError: true);
    }
  }

  internal static void ExportVisible(
    LogViewSnapshot snapshot,
    Window window,
    ToastNotificationService toast,
    Action<string> exportToPath)
  {
    var dialog = new Microsoft.Win32.SaveFileDialog
    {
      Filter = "文本文件 (*.txt)|*.txt",
      FileName = "SmartGuard.log.txt",
      DefaultExt = ".txt",
    };

    if (dialog.ShowDialog(window) != true)
      return;

    exportToPath(dialog.FileName);
  }

  internal static void ExportToPath(LogViewSnapshot snapshot, string destinationPath, ToastNotificationService toast)
  {
    try
    {
      LogViewToolbarActions.ExportVisibleText(
        LogViewToolbarActions.BuildVisibleText(snapshot),
        destinationPath);
      toast.Show("日志已导出", isError: false);
    }
    catch (Exception ex)
    {
      toast.Show($"导出失败：{ex.Message}", isError: true);
    }
  }

  internal static void ExportDiagnosticBundle(
    Window window,
    ToastNotificationService toast,
    DiagnosticBundleRequest requestTemplate,
    Action<string, DiagnosticBundleRequest> exportToPath)
  {
    var dialog = new Microsoft.Win32.SaveFileDialog
    {
      Filter = "ZIP 压缩文件 (*.zip)|*.zip",
      FileName = $"SmartGuard-diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
      DefaultExt = ".zip",
    };

    if (dialog.ShowDialog(window) != true)
      return;

    var overwriteExisting = File.Exists(dialog.FileName);
    exportToPath(
      dialog.FileName,
      requestTemplate with
      {
        DestinationZipPath = dialog.FileName,
        OverwriteExisting = overwriteExisting,
      });
  }

  internal static void OpenLogFolder(string? logPath, string? fallbackLogPath, ToastNotificationService toast)
  {
    if (string.IsNullOrWhiteSpace(logPath))
      return;

    var logFilePath = LogViewToolbarActions.ResolveLogFilePath(logPath, fallbackLogPath);
    try
    {
      System.Diagnostics.Process.Start(LogViewToolbarActions.CreateRevealLogFileProcessStartInfo(logFilePath));
    }
    catch (Exception ex)
    {
      toast.Show($"打开目录失败：{ex.Message}", isError: true);
    }
  }
}
