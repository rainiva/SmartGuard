using System.Diagnostics;
using SmartGuard.Contracts;

namespace SmartGuard.Tray;

public static class RootResolver
{
  public static string Resolve(string[] args)
  {
    for (var i = 0; i < args.Length - 1; i++)
    {
      if (string.Equals(args[i], "--root", StringComparison.OrdinalIgnoreCase))
        return Path.GetFullPath(args[i + 1]);
    }

    var env = Environment.GetEnvironmentVariable("SMARTGUARD_ROOT");
    if (!string.IsNullOrWhiteSpace(env)) return Path.GetFullPath(env);

    var dir = Path.GetFullPath(AppContext.BaseDirectory)
      .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    if (string.Equals(Path.GetFileName(dir), "bin", StringComparison.OrdinalIgnoreCase))
    {
      var installRoot = Directory.GetParent(dir)?.FullName;
      if (!string.IsNullOrWhiteSpace(installRoot))
        return installRoot;
    }

    return dir;
  }
}

public static class ExternalToolLauncher
{
  public static void OpenSettings(string root)
  {
    if (SingleInstanceActivation.TryNotifyExisting("Settings"))
      return;

    var exe = Path.Combine(root, "bin", "SmartGuard.Settings.exe");
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
    if (SingleInstanceActivation.TryNotifyExisting("LogViewer"))
      return;

    var exe = Path.Combine(root, "bin", "SmartGuard.LogViewer.exe");
    if (!File.Exists(exe))
      throw new FileNotFoundException("SmartGuard.LogViewer.exe not found. Reinstall SmartGuard.", exe);

    Process.Start(new ProcessStartInfo
    {
      FileName = exe,
      Arguments = $"--root \"{root}\"",
      WorkingDirectory = root,
      UseShellExecute = false,
    });
  }
}

public static class TrayIconLoader
{
  public static Icon Load(string root)
  {
    var iconPath = Path.Combine(root, "lib", "SmartGuard.ico");
    if (File.Exists(iconPath))
    {
      try { return new Icon(iconPath); } catch { /* fall through */ }
    }

    return SystemIcons.Shield;
  }
}
