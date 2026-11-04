using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace SmartGuard.Engine.Infrastructure;

public sealed class BrightnessService
{
  // A monitor provider can stall for minutes with the infinite WMI default,
  // preventing the guardian from adopting configuration or sampling foreground.
  // This bounds enumeration, not namespace connection or every COM operation.
  private static readonly TimeSpan WmiTimeout = TimeSpan.FromMilliseconds(250);

  private static ManagementObjectSearcher CreateQuery(string query)
    => new(@"root\WMI", query, new System.Management.EnumerationOptions
    {
      ReturnImmediately = true,
      Timeout = WmiTimeout,
      Rewindable = false,
    });

  public int GetBrightness()
  {
    try
    {
      using var searcher = CreateQuery("SELECT CurrentBrightness FROM WmiMonitorBrightness");
      using var results = searcher.Get();
      foreach (ManagementObject obj in results)
      {
        using (obj)
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
      using var searcher = CreateQuery("SELECT * FROM WmiMonitorBrightnessMethods");
      using var results = searcher.Get();
      foreach (ManagementObject method in results)
      {
        using (method)
          method.InvokeMethod("WmiSetBrightness", new object[] { (byte)1, (byte)level });
        break;
      }
    }
    catch
    {
      // ignore
    }
  }

  public int RestoreWithRetry(int targetLevel, int maxAttempts, int delayMs)
  {
    return RestoreWithRetryAsync(targetLevel, maxAttempts, delayMs).GetAwaiter().GetResult();
  }

  public async Task<int> RestoreWithRetryAsync(int targetLevel, int maxAttempts, int delayMs, CancellationToken cancellationToken = default)
  {
    if (targetLevel < 0) return targetLevel;
    var after = targetLevel;
    for (var i = 0; i < maxAttempts; i++)
    {
      SetBrightness(targetLevel);
      await Task.Delay(delayMs, cancellationToken);
      after = GetBrightness();
      if (after == targetLevel) break;
    }
    return after;
  }
}
