using System.Diagnostics;

namespace SmartGuard.Configuration;

public static class EngineLauncher
{
  public static string GetEngineExecutablePath(string root) => SmartGuardPaths.EngineExe(root);

  public static string BuildForegroundArguments(string root) => $"--root \"{root}\"";

  public static int StartForeground(string root)
  {
    var exe = GetEngineExecutablePath(root);
    if (!File.Exists(exe))
    {
      Console.Error.WriteLine($"ERROR: SmartGuard.Engine.exe not found at {exe}");
      Console.Error.WriteLine("Run build.cmd, or reinstall SmartGuard.");
      return 1;
    }

    using var process = Process.Start(new ProcessStartInfo
    {
      FileName = exe,
      Arguments = BuildForegroundArguments(root),
      WorkingDirectory = root,
      UseShellExecute = false,
    });
    if (process is null)
      return 1;

    process.WaitForExit();
    return process.ExitCode;
  }
}
