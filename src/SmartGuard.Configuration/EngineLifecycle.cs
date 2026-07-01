using System.Diagnostics;

namespace SmartGuard.Configuration;

public static class EngineLifecycle
{
  public static void StopProcesses()
  {
    TryKillProcess("SmartGuard.Tray.exe");
    TryKillProcess("SmartGuard.Engine.exe");
    TryKillProcess("SmartGuard.LogViewer.exe");
    TryKillProcess("SmartGuard.Settings.exe");
  }

  public static void DeleteScheduledTasks()
  {
    foreach (var taskName in ScheduledTaskRegistrar.TaskNames)
      TryDeleteScheduledTask(taskName);
  }

  public static void StopForUninstall()
  {
    StopProcesses();
    Thread.Sleep(2000);
    DeleteScheduledTasks();
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
  {
    try
    {
      using var proc = Process.Start(new ProcessStartInfo
      {
        FileName = "schtasks.exe",
        Arguments = $"/Delete /TN \"{taskName}\" /F",
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
