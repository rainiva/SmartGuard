using System.Diagnostics;

namespace SmartGuard.Configuration;

public static class GuardianRecovery
{
  public const string GuardianTaskName = "SmartGuard Guardian";
  public const int MissedStatusThreshold = 3;

  public static bool ShouldAttemptStart(int consecutiveMissedReads)
    => consecutiveMissedReads >= MissedStatusThreshold;

  public static string GetEngineExecutablePath(string root)
    => Path.Combine(root, "bin", "SmartGuard.Engine.exe");

  public static string BuildSchTasksRunArguments()
    => $"/Run /TN \"{GuardianTaskName}\"";

  public static void TryStartGuardian(string root)
  {
    if (TryRunScheduledTask()) return;
    TryLaunchEngine(root);
  }

  private static bool TryRunScheduledTask()
  {
    try
    {
      using var process = Process.Start(new ProcessStartInfo
      {
        FileName = "schtasks.exe",
        Arguments = BuildSchTasksRunArguments(),
        UseShellExecute = false,
        CreateNoWindow = true,
      });
      if (process is null) return false;
      process.WaitForExit();
      return process.ExitCode == 0;
    }
    catch
    {
      return false;
    }
  }

  private static void TryLaunchEngine(string root)
  {
    var engineExe = GetEngineExecutablePath(root);
    if (!File.Exists(engineExe)) return;

    try
    {
      Process.Start(new ProcessStartInfo
      {
        FileName = engineExe,
        Arguments = $"--root \"{root}\"",
        WorkingDirectory = root,
        UseShellExecute = false,
        CreateNoWindow = true,
      });
    }
    catch
    {
      // tray recovery is best-effort
    }
  }
}
