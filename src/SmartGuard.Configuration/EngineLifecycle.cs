using System.Diagnostics;

namespace SmartGuard.Configuration;

public static class EngineLifecycle
{
  private static readonly string[] ProcessImageNames =
  [
    "SmartGuard.Tray.exe",
    "SmartGuard.Engine.exe",
    "SmartGuard.LogViewer.exe",
    "SmartGuard.Settings.exe",
  ];

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
    foreach (var processName in ProcessImageNames)
      TryKillProcess(processName);
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
    foreach (var processName in ProcessImageNames)
    {
      try
      {
        if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName)).Length > 0)
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

  private static void TryKillProcess(string processName)
  {
    try
    {
      var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName));
      foreach (var process in processes)
      {
        try
        {
          process.Kill();
          process.WaitForExit(5000);
        }
        catch
        {
          // ignore individual process kill failures
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

  private static void TryRunSchtasks(string arguments)
  {
    try
    {
      using var proc = Process.Start(new ProcessStartInfo
      {
        FileName = "schtasks.exe",
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
