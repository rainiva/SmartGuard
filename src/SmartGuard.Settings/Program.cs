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
      if (!SingleInstanceActivation.TryNotifyExisting("Settings", startPage))
      {
        AppDialog.ShowAlert(null, "智能电源守护",
          "设置窗口已在运行但未能响应。请在任务管理器中结束 SmartGuard.Settings.exe 后重试。",
          AppDialogSeverity.Warning);
      }

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

    var root = InstallRootResolver.Resolve(null, args);
    var configPath = SmartGuardPaths.ConfigFile(root);
    var repository = new GuardConfigRepository(configPath);
    var config = repository.LoadOrDefault(root);

    controller = SettingsWindowController.TryCreate(root, repository, config, out var loadError);
    if (controller is null)
    {
      AppDialog.ShowAlert(null, "智能电源守护",
        string.IsNullOrWhiteSpace(loadError)
          ? "设置界面加载失败。"
          : $"设置界面加载失败：\n{loadError}",
        AppDialogSeverity.Error);
      activationCts.Cancel();
      return;
    }

    if (!string.IsNullOrEmpty(startPage))
      controller.SetInitialPage(startPage);

    try
    {
      controller.ShowDialog();
    }
    catch (Exception ex)
    {
      AppDialog.ShowAlert(null, "智能电源守护",
        $"设置界面显示失败：\n{ex.Message}",
        AppDialogSeverity.Error);
    }

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
