using Microsoft.Win32;

namespace SmartGuard.Configuration.Shell;

public sealed record DesktopShellShortcut(
  string ShortcutPath,
  string TargetPath,
  string Arguments,
  string WorkingDirectory,
  string Description,
  string AppUserModelId,
  string IconLocation);

public static class DesktopShellRegistration
{
  internal static IReadOnlyList<DesktopShellShortcut> GetStartMenuShortcuts(string root)
  {
    var programsDir = GetProgramsDirectory();
    return
    [
      CreateShortcut(
        Path.Combine(programsDir, "SmartGuard 托盘.lnk"),
        SmartGuardPaths.TrayExe(root),
        $"--root \"{root}\""),
      CreateShortcut(
        Path.Combine(programsDir, "SmartGuard 设置.lnk"),
        SmartGuardPaths.SettingsExe(root),
        $"--root \"{root}\""),
      CreateShortcut(
        Path.Combine(programsDir, "SmartGuard 日志.lnk"),
        SmartGuardPaths.SettingsExe(root),
        $"--root \"{root}\" --page logs"),
    ];
  }

  internal static DesktopShellShortcut GetDesktopShortcut(string root)
    => CreateShortcut(
      Path.Combine(GetDesktopDirectory(), "SmartGuard.lnk"),
      SmartGuardPaths.TrayExe(root),
      $"--root \"{root}\"");

  public static void Apply(string root, bool createDesktopShortcut)
  {
    RegisterAppUserModelId(root);

    foreach (var shortcut in GetStartMenuShortcuts(root))
      WriteShortcut(shortcut);

    var desktopShortcut = GetDesktopShortcut(root);
    if (createDesktopShortcut)
      WriteShortcut(desktopShortcut);
    else
      DeleteShortcut(desktopShortcut.ShortcutPath);

    DeleteLegacyArtifacts(root);
  }

  public static void Remove(string root)
  {
    foreach (var shortcut in GetStartMenuShortcuts(root))
      DeleteShortcut(shortcut.ShortcutPath);

    DeleteShortcut(GetDesktopShortcut(root).ShortcutPath);
    UnregisterAppUserModelId();
    DeleteLegacyArtifacts(root);
  }

  private static DesktopShellShortcut CreateShortcut(string shortcutPath, string targetPath, string arguments)
  {
    var iconLocation = File.Exists(targetPath)
      ? $"{targetPath},0"
      : targetPath;
    return new DesktopShellShortcut(
      shortcutPath,
      targetPath,
      arguments,
      Path.GetDirectoryName(targetPath) ?? string.Empty,
      SmartGuardToastAppId.DisplayName,
      SmartGuardToastAppId.AppId,
      iconLocation);
  }

  private static void RegisterAppUserModelId(string root)
  {
    var regPath = $@"Software\Classes\AppUserModelId\{SmartGuardToastAppId.AppId}";
    using var key = Registry.CurrentUser.CreateSubKey(regPath, true)
      ?? throw new InvalidOperationException($"Unable to open registry key: {regPath}");
    key.SetValue("DisplayName", SmartGuardToastAppId.DisplayName, RegistryValueKind.String);

    var iconTarget = SmartGuardPaths.TrayExe(root);
    if (File.Exists(iconTarget))
      key.SetValue("IconUri", new Uri(iconTarget).AbsoluteUri, RegistryValueKind.String);
  }

  private static void UnregisterAppUserModelId()
  {
    Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\AppUserModelId\{SmartGuardToastAppId.AppId}", false);
  }

  private static void WriteShortcut(DesktopShellShortcut shortcut)
  {
    if (!File.Exists(shortcut.TargetPath))
      throw new FileNotFoundException("Shell shortcut target not found.", shortcut.TargetPath);

    var directory = Path.GetDirectoryName(shortcut.ShortcutPath);
    if (!string.IsNullOrEmpty(directory))
      Directory.CreateDirectory(directory);

    var shellType = Type.GetTypeFromProgID("WScript.Shell")
      ?? throw new InvalidOperationException("WScript.Shell is not available.");
    dynamic shell = Activator.CreateInstance(shellType)!;
    dynamic link = shell.CreateShortcut(shortcut.ShortcutPath);
    link.TargetPath = shortcut.TargetPath;
    link.Arguments = shortcut.Arguments;
    link.WorkingDirectory = shortcut.WorkingDirectory;
    link.Description = shortcut.Description;
    link.IconLocation = shortcut.IconLocation;
    link.Save();

    ShellLinkAppUserModelId.Apply(shortcut.ShortcutPath, shortcut.AppUserModelId);
  }

  private static void DeleteLegacyArtifacts(string root)
  {
    DeleteShortcut(Path.Combine(GetProgramsDirectory(), "SmartGuard.lnk"));

    var legacyGroupDir = Path.Combine(GetProgramsDirectory(), "SmartGuard");
    foreach (var name in new[] { "SmartGuard 托盘.lnk", "SmartGuard 设置.lnk", "SmartGuard 日志.lnk", "卸载 SmartGuard.lnk" })
      DeleteShortcut(Path.Combine(legacyGroupDir, name));
    DeleteDirectoryIfEmpty(legacyGroupDir);

    foreach (var markerPath in new[]
    {
      Path.Combine(root, "lib", ".smartguard-toast-shortcut"),
      Path.Combine(root, "lib", ".smartguard-toast-aumid"),
    })
    {
      try
      {
        if (File.Exists(markerPath))
          File.Delete(markerPath);
      }
      catch
      {
        // best-effort cleanup of legacy runtime markers
      }
    }
  }

  private static void DeleteShortcut(string shortcutPath)
  {
    try
    {
      if (File.Exists(shortcutPath))
        File.Delete(shortcutPath);
    }
    catch
    {
      // shortcut cleanup is best-effort
    }
  }

  private static void DeleteDirectoryIfEmpty(string directoryPath)
  {
    try
    {
      if (Directory.Exists(directoryPath) &&
          !Directory.EnumerateFileSystemEntries(directoryPath).Any())
      {
        Directory.Delete(directoryPath, false);
      }
    }
    catch
    {
      // best-effort cleanup
    }
  }

  private static string GetProgramsDirectory()
    => Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "Microsoft",
      "Windows",
      "Start Menu",
      "Programs");

  private static string GetDesktopDirectory()
    => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
}
