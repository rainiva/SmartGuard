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

    var root = RootResolver.Resolve(args);
    var config = new GuardConfigRepository(Path.Combine(root, "SmartGuard.config.json")).LoadOrDefault(root);
    var logPath = string.IsNullOrWhiteSpace(config.LogFile)
      ? Path.Combine(root, "SmartGuard.log")
      : config.LogFile;
    var fallback = Path.Combine(root, "SmartGuard.startup.log");

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

internal static class RootResolver
{
  public static string Resolve(string[] args)
  {
    for (var i = 0; i < args.Length - 1; i++)
    {
      if (string.Equals(args[i], "--root", StringComparison.OrdinalIgnoreCase))
        return Path.GetFullPath(args[i + 1]);
    }

    var env = Environment.GetEnvironmentVariable("SMARTGUARD_ROOT");
    if (!string.IsNullOrWhiteSpace(env)) return Path.GetFullPath(env);
    return Path.GetFullPath(AppContext.BaseDirectory);
  }
}
