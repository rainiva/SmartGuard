using SmartGuard.Configuration;

namespace SmartGuard.Tray.Toast;

internal static class StartMenuShortcutWriter
{
  internal static void EnsureShortcut(
    string root,
    Func<string, bool>? testWriter = null,
    Action? onWriteForTests = null)
  {
    if (File.Exists(GetShortcutMarkerPath(root)))
      return;

    var programs = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "Microsoft", "Windows", "Start Menu", "Programs");
    if (!Directory.Exists(programs)) return;

    var shortcutPath = Path.Combine(programs, "SmartGuard.lnk");
    if (testWriter?.Invoke(shortcutPath) == true)
    {
      MarkShortcutRegistered(root, onWriteForTests);
      return;
    }

    var shortcutTarget = ToastShortcutResolver.Resolve(root);

    var shellType = Type.GetTypeFromProgID("WScript.Shell");
    if (shellType is null) return;
    dynamic shell = Activator.CreateInstance(shellType)!;
    dynamic shortcut = shell.CreateShortcut(shortcutPath);
    shortcut.TargetPath = shortcutTarget.TargetPath;
    shortcut.Arguments = shortcutTarget.Arguments;
    shortcut.WorkingDirectory = shortcutTarget.WorkingDirectory;
    shortcut.Description = SmartGuardToastAppId.DisplayName;
    var iconPath = SmartGuardPaths.BrandIcon(root);
    if (File.Exists(iconPath))
      shortcut.IconLocation = $"{iconPath},0";
    shortcut.Save();

    ShellLinkAppUserModelId.Apply(shortcutPath, SmartGuardToastAppId.AppId);
    MarkShortcutRegistered(root, onWriteForTests);
  }

  private static string GetShortcutMarkerPath(string root)
    => Path.Combine(root, "lib", ".smartguard-toast-shortcut");

  private static void MarkShortcutRegistered(string root, Action? onWriteForTests)
  {
    onWriteForTests?.Invoke();
    var markerPath = GetShortcutMarkerPath(root);
    var markerDir = Path.GetDirectoryName(markerPath);
    if (!string.IsNullOrEmpty(markerDir))
      Directory.CreateDirectory(markerDir);
    File.WriteAllText(markerPath, DateTime.UtcNow.ToString("o"));
  }
}
