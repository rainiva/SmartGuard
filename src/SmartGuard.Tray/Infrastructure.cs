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
  private static string? _cachedIconPath;
  private static Icon? _cachedIcon;

  internal static int LoadCountForTests { get; private set; }

  internal static void ResetCacheForTests()
  {
    _cachedIcon?.Dispose();
    _cachedIcon = null;
    _cachedIconPath = null;
    LoadCountForTests = 0;
  }

  public static Icon Load(string root)
  {
    var iconPath = SmartGuardPaths.BrandIcon(root);
    if (_cachedIcon is not null && string.Equals(_cachedIconPath, iconPath, StringComparison.OrdinalIgnoreCase))
      return (Icon)_cachedIcon.Clone();

    LoadCountForTests++;
    if (File.Exists(iconPath))
    {
      try
      {
        var icon = new Icon(iconPath);
        _cachedIcon?.Dispose();
        _cachedIcon = icon;
        _cachedIconPath = iconPath;
        return (Icon)icon.Clone();
      }
      catch { /* fall through */ }
    }

    return (Icon)SystemIcons.Shield.Clone();
  }
}
