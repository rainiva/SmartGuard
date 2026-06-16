using System.Management;

namespace SmartGuard.Engine.Infrastructure;

public sealed class BrightnessService
{
  public int GetBrightness()
  {
    try
    {
      using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
      foreach (var obj in searcher.Get().Cast<ManagementObject>())
      {
        return Convert.ToInt32(obj["CurrentBrightness"]);
      }
    }
    catch
    {
      // WMI not available
    }
    return -1;
  }

  public void SetBrightness(int level)
  {
    if (level < 0 || level > 100) return;
    try
    {
      using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
      var method = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
      method?.InvokeMethod("WmiSetBrightness", new object[] { (byte)1, (byte)level });
    }
    catch
    {
      // ignore
    }
  }

  public int RestoreWithRetry(int targetLevel, int maxAttempts, int delayMs)
  {
    if (targetLevel < 0) return targetLevel;
    var after = targetLevel;
    for (var i = 0; i < maxAttempts; i++)
    {
      SetBrightness(targetLevel);
      Thread.Sleep(delayMs);
      after = GetBrightness();
      if (after == targetLevel) break;
    }
    return after;
  }
}
