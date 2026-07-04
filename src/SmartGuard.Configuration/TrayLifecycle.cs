using System.Diagnostics;

namespace SmartGuard.Configuration;

public static class TrayLifecycle
{
  public static string TrayProcessImageName => SmartGuardPaths.TrayExeFileName;

  public static void Restart(string root)
  {
    StopTrayProcesses();
    Thread.Sleep(TimeSpan.FromSeconds(1));
    TrayLauncher.Start(root);
  }

  private static void StopTrayProcesses()
  {
    try
    {
      var processes = Process.GetProcessesByName(
        Path.GetFileNameWithoutExtension(TrayProcessImageName));
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
}
