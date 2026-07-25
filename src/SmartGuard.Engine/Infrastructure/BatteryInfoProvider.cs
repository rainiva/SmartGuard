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
      return ResolveFromSources(TryGetNativeBatteryInfo, TryGetWmiBatteryInfo);
    }
    catch
    {
      return (100, true);
    }
  }

  internal static (int Percent, bool IsOnAc) ResolveFromSources(
    Func<(byte AcLineStatus, byte BatteryLifePercent, byte BatteryFlag)?> readNativeStatus,
    Func<(int Percent, bool? IsOnAc)?> readWmiStatus)
  {
    var native = readNativeStatus();
    if (native is { } completeStatus &&
        !BatteryStatusInterpreter.IsNoSystemBattery(completeStatus.BatteryFlag) &&
        BatteryStatusInterpreter.InterpretAcLineStatus(completeStatus.AcLineStatus) is { } nativeIsOnAc &&
        BatteryStatusInterpreter.InterpretBatteryLifePercent(completeStatus.BatteryLifePercent) is { } nativePercent)
    {
      return (nativePercent, nativeIsOnAc);
    }

    (int Percent, bool? IsOnAc)? wmi = null;
    try
    {
      wmi = readWmiStatus();
    }
    catch
    {
      // WMI is optional enrichment. A failed probe must not discard native status.
    }

    if (native is { } status)
    {
      return BatteryStatusInterpreter.Resolve(
        status.AcLineStatus,
        status.BatteryLifePercent,
        status.BatteryFlag,
        wmi?.Percent,
        wmi?.IsOnAc);
    }

    return wmi is { } fallback
      ? (fallback.Percent, fallback.IsOnAc ?? true)
      : (100, true);
  }

  private static (byte AcLineStatus, byte BatteryLifePercent, byte BatteryFlag)? TryGetNativeBatteryInfo()
  {
    var status = new SystemPowerStatus();
    if (!GetSystemPowerStatus(ref status))
      return null;

    return (status.ACLineStatus, status.BatteryLifePercent, status.BatteryFlag);
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
