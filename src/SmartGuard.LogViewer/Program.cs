using SmartGuard.Configuration;

namespace SmartGuard.LogViewer;

internal static class Program
{
  [STAThread]
  private static void Main(string[] args)
  {
    var root = InstallRootResolver.Resolve(null, args);
    LogViewerEntryRedirect.LaunchSettingsLogsPage(root);
  }
}
