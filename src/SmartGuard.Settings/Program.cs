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
    var startPage = ParseStartPage(args);

    using var guard = SingleInstanceGuard.TryAcquire("Settings");
    if (!guard.IsOwner)
    {
      SingleInstanceActivation.TryNotifyExisting("Settings", startPage);
      return;
    }

    var app = new Application();
    using var activationCts = new CancellationTokenSource();
    SettingsWindowController? controller = null;
    var activationThread = new Thread(() =>
      SingleInstanceActivation.RunActivationServer(
        "Settings",
        (argument) =>
        {
          if (!string.IsNullOrEmpty(argument))
            controller?.NavigateTo(argument);
          controller?.Activate();
        },
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

    if (!string.IsNullOrEmpty(startPage))
      controller.NavigateTo(startPage);

    if (controller.ShowDialog() == true)
      controller.CommitPendingSave();

    activationCts.Cancel();
    activationThread.Join(TimeSpan.FromSeconds(1));
  }

  private static string ParseStartPage(string[] args)
  {
    for (var i = 0; i < args.Length - 1; i++)
    {
      if (string.Equals(args[i], "--page", StringComparison.OrdinalIgnoreCase))
        return args[i + 1];
    }
    return string.Empty;
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
