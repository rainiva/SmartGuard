using SmartGuard.Configuration;

namespace SmartGuard.LogViewer;

internal static class LogViewerEntryRedirect
{
  internal static void LaunchSettingsLogsPage(string root) => SettingsLogsPageLauncher.Open(root);
}
