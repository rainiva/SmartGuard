using System.Diagnostics;

namespace SmartGuard.Configuration;

public static class GuardianRecovery
{
  public const int MissedStatusThreshold = 3;

  public static bool ShouldAttemptStart(int consecutiveMissedReads)
    => consecutiveMissedReads >= MissedStatusThreshold;

  public static string GetEngineExecutablePath(string root)
    => SmartGuardPaths.EngineExe(root);

  public static string BuildSchTasksRunArguments()
    => $"/Run /TN \"{ScheduledTaskRegistrar.GuardianTaskName}\"";

  public static void TryStartGuardian(string root)
    => TryRunScheduledTask();

  private static void TryRunScheduledTask()
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
      process?.WaitForExit();
    }
    catch
    {
      // tray recovery is best-effort
    }
  }
}
