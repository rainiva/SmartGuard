using System.Diagnostics;
using SmartGuard.Contracts;

namespace SmartGuard.Configuration;

public static class SettingsMainPageLauncher
{
  public static void Open(string root)
  {
    if (SingleInstanceActivation.TryNotifyExisting("Settings"))
      return;

    var exe = SmartGuardPaths.SettingsExe(root);
    if (!File.Exists(exe))
      throw new FileNotFoundException("SmartGuard.Settings.exe not found. Reinstall SmartGuard.", exe);

    Process.Start(new ProcessStartInfo
    {
      FileName = exe,
      Arguments = $"--root \"{root}\"",
      WorkingDirectory = root,
      UseShellExecute = false,
    });
  }
}
