using System.Diagnostics;

namespace SmartGuard.Configuration;

public static class EngineLifecycle
{
  public static void EndAndDisableScheduledTasks()
  {
    foreach (var taskName in ScheduledTaskRegistrar.TaskNames)
    {
      TryRunSchtasks($"/End /TN \"{taskName}\" /F");
      TryRunSchtasks($"/Change /TN \"{taskName}\" /Disable");
    }
  }

  public static void StopProcesses()
  {
    var currentProcessId = Environment.ProcessId;
    foreach (var processName in SmartGuardPaths.ProcessImageNames)
      TryKillProcess(processName, currentProcessId);
  }

  public static void DeleteScheduledTasks()
  {
    foreach (var taskName in ScheduledTaskRegistrar.TaskNames)
      TryDeleteScheduledTask(taskName);
  }

  public static void StopForUninstall()
  {
    EndAndDisableScheduledTasks();
    StopProcesses();
    WaitForProcessesToExit(TimeSpan.FromSeconds(3));
    DeleteScheduledTasks();
  }

  public static bool AnySmartGuardProcessRunning()
  {
    var currentProcessId = Environment.ProcessId;
    foreach (var processName in SmartGuardPaths.ProcessImageNames)
    {
      try
      {
        if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName))
                   .Any(process => process.Id != currentProcessId))
          return true;
      }
      catch
      {
        // ignore probe failures
      }
    }

    return false;
  }

  private static void WaitForProcessesToExit(TimeSpan timeout)
  {
    var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
    while (Environment.TickCount64 < deadline)
    {
      if (!AnySmartGuardProcessRunning())
        return;
      Thread.Sleep(50);
    }
  }

  private static void TryKillProcess(string processName, int currentProcessId)
  {
    try
    {
      var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName));
      foreach (var process in processes)
      {
        if (process.Id == currentProcessId)
          continue;

        try
        {
          process.Kill(entireProcessTree: true);
          if (!process.WaitForExit(5000))
            TryForceKillProcessById(process.Id);
        }
        catch
        {
          TryForceKillProcessById(process.Id);
        }
      }
    }
    catch
    {
      // ignore
    }
  }

  private static void TryDeleteScheduledTask(string taskName)
    => TryRunSchtasks($"/Delete /TN \"{taskName}\" /F");

  private static void TryForceKillProcessById(int processId)
    => TryRunProcess("taskkill.exe", $"/F /T /PID {processId}");

  private static void TryRunSchtasks(string arguments)
    => TryRunProcess("schtasks.exe", arguments);

  private static void TryRunProcess(string fileName, string arguments)
  {
    try
    {
      using var proc = Process.Start(new ProcessStartInfo
      {
        FileName = fileName,
        Arguments = arguments,
        UseShellExecute = false,
        CreateNoWindow = true,
      });
      proc?.WaitForExit();
    }
    catch
    {
      // task may already be absent
    }
  }
}
