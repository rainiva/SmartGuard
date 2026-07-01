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

  public static bool ShouldSkipScheduledTaskRecovery(int enginePid)
  {
    if (enginePid <= 0)
      return false;

    if (EngineProcessCheckerForTests is not null)
      return EngineProcessCheckerForTests(enginePid);

    try
    {
      using var process = Process.GetProcessById(enginePid);
      return process.ProcessName.Equals("SmartGuard.Engine", StringComparison.OrdinalIgnoreCase);
    }
    catch
    {
      return false;
    }
  }

  internal static Func<int, bool>? EngineProcessCheckerForTests;

  internal static void ResetEngineProcessCheckerForTests() => EngineProcessCheckerForTests = null;

  public static bool ShouldSkipScheduledTaskRecovery(string root)
  {
    var status = StatusJsonReader.TryRead(SmartGuardPaths.StatusFile(root));
    return status is not null && ShouldSkipScheduledTaskRecovery(status.enginePid);
  }

  public static void TryStartGuardian(string root)
  {
    if (ShouldSkipScheduledTaskRecovery(root))
      return;

    TryRunScheduledTask();
  }

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
