using System.Windows;

namespace SmartGuard.Settings;

internal static class SettingsLogExportActions
{
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
