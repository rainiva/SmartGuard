using System.Diagnostics;
using SmartGuard.Contracts;

namespace SmartGuard.Configuration;

public static class SettingsLogsPageLauncher
{
  public static void Open(string root)
  {
    if (SingleInstanceActivation.TryNotifyExisting("Settings", "logs"))
      return;

    var exe = SmartGuardPaths.SettingsExe(root);
    if (!File.Exists(exe))
      throw new FileNotFoundException("SmartGuard.Settings.exe not found. Reinstall SmartGuard.", exe);

    Process.Start(new ProcessStartInfo
    {
      FileName = exe,
      Arguments = $"--root \"{root}\" --page logs",
      WorkingDirectory = root,
      UseShellExecute = false,
    });
  }
}
