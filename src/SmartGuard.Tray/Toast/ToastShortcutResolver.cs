using SmartGuard.Configuration;

namespace SmartGuard.Tray.Toast;

public sealed record ToastShortcutTarget(string TargetPath, string Arguments, string WorkingDirectory);

public static class ToastShortcutResolver
{
  public static ToastShortcutTarget Resolve(string root)
  {
    var workingDirectory = root;
    var trayExe = SmartGuardPaths.TrayExe(root);
    if (File.Exists(trayExe))
    {
      return new ToastShortcutTarget(trayExe, $"--root \"{root}\"", workingDirectory);
    }

    var startTray = Path.Combine(root, "Start-Tray.cmd");
    if (File.Exists(startTray))
    {
      return new ToastShortcutTarget(startTray, string.Empty, workingDirectory);
    }

    var restartTray = Path.Combine(root, "Restart-Tray.cmd");
    if (File.Exists(restartTray))
    {
      return new ToastShortcutTarget(restartTray, string.Empty, workingDirectory);
    }

    throw new FileNotFoundException(
      "SmartGuard tray launcher not found. Expected bin\\SmartGuard.Tray.exe or Start-Tray.cmd.",
      trayExe);
  }
}
