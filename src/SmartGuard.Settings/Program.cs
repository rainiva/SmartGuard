using System.IO;
using System.Threading;
using System.Windows;
using SmartGuard.Configuration;
using SmartGuard.Contracts;

namespace SmartGuard.Settings;

internal static class Program
{
  [STAThread]
  private static void Main(string[] args)
  {
    using var guard = SingleInstanceGuard.TryAcquire("Settings");
    if (!guard.IsOwner)
    {
      SingleInstanceActivation.TryNotifyExisting("Settings");
      return;
    }

    var app = new Application();
    using var activationCts = new CancellationTokenSource();
    SettingsWindowController? controller = null;
    var activationThread = new Thread(() =>
      SingleInstanceActivation.RunActivationServer(
        "Settings",
        () => controller?.Activate(),
        activationCts.Token))
    {
      IsBackground = true,
    };
    activationThread.Start();

    var root = RootResolver.Resolve(args);
    var configPath = Path.Combine(root, "SmartGuard.config.json");
    var repository = new GuardConfigRepository(configPath);
    var config = repository.LoadOrDefault(root);

    controller = SettingsWindowController.TryCreate(root, repository, config);
    if (controller is null)
    {
      MessageBox.Show(
        "设置界面加载失败。",
        "智能电源守护",
        MessageBoxButton.OK,
        MessageBoxImage.Error);
      activationCts.Cancel();
      return;
    }

    if (controller.ShowDialog() == true)
      controller.CommitPendingSave();

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
