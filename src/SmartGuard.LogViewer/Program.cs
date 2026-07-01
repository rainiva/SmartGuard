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

    using var guard = SingleInstanceGuard.TryAcquire("LogViewer");
    if (!guard.IsOwner)
    {
      SingleInstanceActivation.TryNotifyExisting("LogViewer");
      return;
    }

    var root = InstallRootResolver.Resolve(null, args);
    var repository = new GuardConfigRepository(SmartGuardPaths.ConfigFile(root));
    var config = repository.LoadOrDefault(root);
    var logPath = SmartGuardPaths.ResolveLogFile(config, root);
    var fallback = SmartGuardPaths.StartupLogFile(root);

    using var activationCts = new CancellationTokenSource();
    var form = new LogViewerForm(root, logPath, fallback);
    var activationThread = new Thread(() =>
      SingleInstanceActivation.RunActivationServer(
        "LogViewer",
        () =>
        {
          if (form.IsHandleCreated && !form.IsDisposed)
            form.BeginInvoke(form.ActivateWindow);
        },
        activationCts.Token))
    {
      IsBackground = true,
    };
    activationThread.Start();

    Application.Run(form);

    activationCts.Cancel();
    activationThread.Join(TimeSpan.FromSeconds(1));
  }
}
