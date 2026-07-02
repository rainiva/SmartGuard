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

    var cliExe = SmartGuardPaths.CliExe(root);
    if (File.Exists(cliExe))
    {
      return new ToastShortcutTarget(cliExe, $"open tray --root \"{root}\"", workingDirectory);
    }

    throw new FileNotFoundException(
      "SmartGuard tray launcher not found. Expected bin\\SmartGuard.Tray.exe or bin\\SmartGuard.Cli.exe.",
      trayExe);
  }
}
