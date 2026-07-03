using Microsoft.Win32;
using System.Runtime.InteropServices;

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
        Path.Combine(programsDir, "SmartGuard.lnk"),
        SmartGuardPaths.TrayExe(root),
        $"--root \"{root}\""),
    ];
  }

  internal static DesktopShellShortcut GetDesktopShortcut(string root)
    => CreateShortcut(
      Path.Combine(GetDesktopDirectory(), "SmartGuard.lnk"),
      SmartGuardPaths.TrayExe(root),
      $"--root \"{root}\"");

  public static void Apply(string root, bool createDesktopShortcut)
  {
    DeleteLegacyArtifacts(root);
    RegisterAppUserModelId(root);

    foreach (var shortcut in GetStartMenuShortcuts(root))
      WriteShortcut(shortcut);

    var desktopShortcut = GetDesktopShortcut(root);
    if (createDesktopShortcut)
      WriteShortcut(desktopShortcut);
    else
      DeleteShortcut(desktopShortcut.ShortcutPath);
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
    dynamic? shell = null;
    dynamic? link = null;
    try
    {
      shell = Activator.CreateInstance(shellType)!;
      link = shell.CreateShortcut(shortcut.ShortcutPath);
      link.TargetPath = shortcut.TargetPath;
      link.Arguments = shortcut.Arguments;
      link.WorkingDirectory = shortcut.WorkingDirectory;
      link.Description = shortcut.Description;
      link.IconLocation = shortcut.IconLocation;
      link.Save();
    }
    finally
    {
      if (link is not null && Marshal.IsComObject(link))
        Marshal.FinalReleaseComObject(link);

      if (shell is not null && Marshal.IsComObject(shell))
        Marshal.FinalReleaseComObject(shell);
    }

    ShellLinkAppUserModelId.Apply(shortcut.ShortcutPath, shortcut.AppUserModelId);
  }

  private static void DeleteLegacyArtifacts(string root)
  {
    foreach (var shortcutPath in new[]
    {
      Path.Combine(GetProgramsDirectory(), "SmartGuard 托盘.lnk"),
      Path.Combine(GetProgramsDirectory(), "SmartGuard 设置.lnk"),
      Path.Combine(GetProgramsDirectory(), "SmartGuard 日志.lnk"),
      Path.Combine(GetProgramsDirectory(), "SmartGuard.lnk"),
    })
      DeleteShortcut(shortcutPath);

    var legacyGroupDir = Path.Combine(GetProgramsDirectory(), "SmartGuard");
    foreach (var name in new[] { "SmartGuard 托盘.lnk", "SmartGuard 设置.lnk", "SmartGuard 日志.lnk", "卸载 SmartGuard.lnk" })
      DeleteShortcut(Path.Combine(legacyGroupDir, name));
    DeleteDirectoryTree(legacyGroupDir);

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
    for (var attempt = 0; attempt < 3; attempt++)
    {
      try
      {
        if (!File.Exists(shortcutPath))
          return;

        File.SetAttributes(shortcutPath, FileAttributes.Normal);
        File.Delete(shortcutPath);
        if (!File.Exists(shortcutPath))
          return;
      }
      catch
      {
        if (attempt == 2)
          return;
      }

      System.Threading.Thread.Sleep(50);
    }
  }

  private static void DeleteDirectoryTree(string directoryPath)
  {
    if (!Directory.Exists(directoryPath))
      return;

    foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
    {
      DeleteShortcut(filePath);
    }

    foreach (var childDirectory in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.AllDirectories)
                                           .OrderByDescending(path => path.Length))
    {
      TryDeleteEmptyDirectory(childDirectory);
    }

    TryDeleteEmptyDirectory(directoryPath);
  }

  private static void TryDeleteEmptyDirectory(string directoryPath)
  {
    for (var attempt = 0; attempt < 3; attempt++)
    {
      try
      {
        if (!Directory.Exists(directoryPath))
          return;

        if (Directory.EnumerateFileSystemEntries(directoryPath).Any())
          return;

        Directory.Delete(directoryPath, false);
        if (!Directory.Exists(directoryPath))
          return;
      }
      catch
      {
        if (attempt == 2)
          return;
      }

      System.Threading.Thread.Sleep(50);
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
