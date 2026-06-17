namespace SmartGuard.Tray.Toast;

public sealed record ToastShortcutTarget(string TargetPath, string Arguments, string WorkingDirectory);

public static class ToastShortcutResolver
{
  public static ToastShortcutTarget Resolve(string root)
  {
    var workingDirectory = root;
    var trayExe = Path.Combine(root, "bin", "SmartGuard.Tray.exe");
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

    return new ToastShortcutTarget("powershell.exe", string.Empty, workingDirectory);
  }
}
