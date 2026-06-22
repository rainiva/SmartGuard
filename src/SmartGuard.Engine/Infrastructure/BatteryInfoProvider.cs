using System.Management;
using System.Runtime.InteropServices;

namespace SmartGuard.Engine.Infrastructure;

public static class BatteryInfoProvider
{
  private static (int Percent, bool IsOnAc)? _cache;
  private static DateTime _cacheAt = DateTime.MinValue;
  private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);
  internal static Func<(int Percent, bool IsOnAc)>? ReadCoreOverrideForTests;

  [StructLayout(LayoutKind.Sequential)]
  private struct SystemPowerStatus
  {
    public byte ACLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte Reserved1;
    public int BatteryLifeTime;
    public int BatteryFullLifeTime;
  }

  [DllImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool GetSystemPowerStatus(ref SystemPowerStatus status);

  public static (int Percent, bool IsOnAc) GetBatteryInfo(bool forceRefresh = false)
  {
    if (!forceRefresh && _cache is { } cached && DateTime.UtcNow - _cacheAt < CacheTtl)
      return cached;

    var result = ReadCore();
    _cache = result;
    _cacheAt = DateTime.UtcNow;
    return result;
  }

  internal static void InvalidateCache()
  {
    _cache = null;
    _cacheAt = DateTime.MinValue;
  }

  internal static void SetCacheForTests(int percent, bool isOnAc)
  {
    _cache = (percent, isOnAc);
    _cacheAt = DateTime.UtcNow;
  }

  private static (int Percent, bool IsOnAc) ReadCore()
  {
    if (ReadCoreOverrideForTests is { } readOverride)
      return readOverride();

    try
    {
      var wmi = TryGetWmiBatteryInfo();
      var status = new SystemPowerStatus();
      if (!GetSystemPowerStatus(ref status))
      {
        return wmi is { } f ? (f.Percent, f.IsOnAc ?? true) : (100, true);
      }

      return BatteryStatusInterpreter.Resolve(
        status.ACLineStatus,
        status.BatteryLifePercent,
        status.BatteryFlag,
        wmi?.Percent,
        wmi?.IsOnAc);
    }
    catch
    {
      return (100, true);
    }
  }

  private static (int Percent, bool? IsOnAc)? TryGetWmiBatteryInfo()
  {
    using var searcher = new ManagementObjectSearcher(
      "SELECT EstimatedChargeRemaining, BatteryStatus, DesignCapacity FROM Win32_Battery");
    var batteries = searcher.Get().Cast<ManagementObject>().ToList();
    if (batteries.Count == 0) return null;

    var entries = new List<(int Percent, uint Weight)>();
    bool? onAc = null;
    foreach (var battery in batteries)
    {
      var percent = Convert.ToInt32(battery["EstimatedChargeRemaining"] ?? 0);
      var design = Convert.ToUInt32(battery["DesignCapacity"] ?? 0);
      if (design == 0) design = 1;
      entries.Add((percent, design));

      if (battery["BatteryStatus"] is { } rawStatus)
      {
        var hint = BatteryStatusInterpreter.InterpretWmiBatteryStatus(Convert.ToInt32(rawStatus));
        if (hint is true) onAc = true;
        else if (hint is false && onAc != true) onAc = false;
      }
    }

    return (BatteryStatusInterpreter.AggregateEstimatedChargeRemaining(entries), onAc);
  }
}
