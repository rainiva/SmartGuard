using System.IO;
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

    var ran = DesktopAppBootstrap.RunSingleInstanceApp(
      "Settings",
      () =>
      {
        if (!SingleInstanceActivation.TryNotifyExisting("Settings", startPage))
        {
          AppDialog.ShowAlert(null, "智能电源守护",
            "设置窗口已在运行但未能响应。请在任务管理器中结束 SmartGuard.Settings.exe 后重试。",
            AppDialogSeverity.Warning);
        }

        return false;
      },
      argument =>
      {
        if (_controller is not null)
        {
          if (!string.IsNullOrEmpty(argument))
            _controller.NavigateTo(argument);
          _controller.Activate();
        }
        else if (!string.IsNullOrEmpty(argument))
        {
          _pendingStartPage = argument;
        }
      },
      () =>
      {
        var app = new Application();
        var root = InstallRootResolver.Resolve(null, args);
        var configPath = SmartGuardPaths.ConfigFile(root);
        var repository = new GuardConfigRepository(configPath);
        var config = repository.LoadOrDefault(root);

        _controller = SettingsWindowController.TryCreate(root, repository, config, out var loadError);
        if (_controller is null)
        {
          AppDialog.ShowAlert(null, "智能电源守护",
            string.IsNullOrWhiteSpace(loadError)
              ? "设置界面加载失败。"
              : $"设置界面加载失败：\n{loadError}",
            AppDialogSeverity.Error);
          return;
        }

        var page = string.IsNullOrEmpty(startPage) ? _pendingStartPage : startPage;
        if (!string.IsNullOrEmpty(page))
          _controller.SetInitialPage(page);

        try
        {
          _controller.ShowDialog();
        }
        catch (Exception ex)
        {
          AppDialog.ShowAlert(null, "智能电源守护",
            $"设置界面显示失败：\n{ex.Message}",
            AppDialogSeverity.Error);
        }
      });

    if (!ran)
      return;
  }

  private static SettingsWindowController? _controller;
  private static string _pendingStartPage = string.Empty;

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
