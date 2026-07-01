using SmartGuard.Configuration;
using SmartGuard.Contracts;

namespace SmartGuard.LogViewer;

internal static class Program
{
  [STAThread]
  private static void Main(string[] args)
  {
    ApplicationConfiguration.Initialize();
    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
    Application.SetCompatibleTextRenderingDefault(false);

    LogViewerForm? form = null;

    var ran = DesktopAppBootstrap.RunSingleInstanceApp(
      "LogViewer",
      () =>
      {
        SingleInstanceActivation.TryNotifyExisting("LogViewer");
        return false;
      },
      _ =>
      {
        if (form is not null && form.IsHandleCreated && !form.IsDisposed)
          form.BeginInvoke(form.ActivateWindow);
      },
      () =>
      {
        var root = InstallRootResolver.Resolve(null, args);
        var repository = new GuardConfigRepository(SmartGuardPaths.ConfigFile(root));
        var config = repository.LoadOrDefault(root);
        var logPath = SmartGuardPaths.ResolveLogFile(config, root);
        var fallback = SmartGuardPaths.StartupLogFile(root);

        form = new LogViewerForm(root, logPath, fallback);
        Application.Run(form);
      });

    if (!ran)
      return;
  }
}
