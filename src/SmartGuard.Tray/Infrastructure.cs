using System.Diagnostics;
using SmartGuard.Configuration;
using SmartGuard.Contracts;

namespace SmartGuard.Tray;

public static class ExternalToolLauncher
{
  public static void OpenSettings(string root)
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

  public static void OpenLogViewer(string root)
  {
    // Open Settings window navigated to logs page (unified WinUI 3 interface)
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

public static class TrayIconLoader
{
  internal static int LoadCountForTests => BrandIconLoader.WinFormsLoadCountForTests;

  internal static void ResetCacheForTests() => BrandIconLoader.ResetCacheForTests();

  public static Icon Load(string root)
    => BrandIconLoader.LoadWinFormsIcon(root, SystemIcons.Shield);
}
