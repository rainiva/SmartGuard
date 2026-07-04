using System.Diagnostics;

namespace SmartGuard.Configuration;

public static class GuardianTaskRunner
{
  public static string BuildSchTasksRunArguments()
    => $"/Run /TN \"{ScheduledTaskRegistrar.GuardianTaskName}\"";

  public static void Run()
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
      // best-effort scheduled task start
    }
  }
}
