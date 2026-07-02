using System.Diagnostics;

namespace SmartGuard.Configuration;

public static class TrayLauncher
{
  public static string GetTrayExecutablePath(string root) => SmartGuardPaths.TrayExe(root);

  public static string BuildStartArguments(string root) => $"--root \"{root}\"";

  public static void Start(string root)
  {
    var exe = GetTrayExecutablePath(root);
    if (!File.Exists(exe))
      throw new FileNotFoundException("SmartGuard.Tray.exe not found. Run build.cmd or reinstall SmartGuard.", exe);

    Process.Start(new ProcessStartInfo
    {
      FileName = exe,
      Arguments = BuildStartArguments(root),
      WorkingDirectory = root,
      UseShellExecute = true,
    });
  }
}
