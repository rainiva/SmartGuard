using System.Management;
using SmartGuard.Engine.Config;
using SmartGuard.Engine.Domain;

namespace SmartGuard.Engine.Infrastructure;

public static class BatteryInfoProvider
{
  public static (int Percent, bool IsOnAc) GetBatteryInfo()
  {
    try
    {
      using var searcher = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
      var battery = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
      if (battery is null) return (100, true);
      var percent = Convert.ToInt32(battery["EstimatedChargeRemaining"]);
      var status = $"{battery["BatteryStatus"]}";
      var onAc = status is "2" or "3" or "6" or "7" or "8" or "9";
      return (percent, onAc);
    }
    catch
    {
      return (100, true);
    }
  }
}
