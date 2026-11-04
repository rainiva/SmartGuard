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
    => GetBatteryInfo(forceRefresh, CancellationToken.None);

  internal static (int Percent, bool IsOnAc) GetBatteryInfo(bool forceRefresh,
    CancellationToken cancellationToken, WmiProbeProcess? probe = null)
  {
    if (!forceRefresh && _cache is { } cached && DateTime.UtcNow - _cacheAt < CacheTtl)
      return cached;

    var result = ReadCore(cancellationToken, probe ?? WmiProbeProcess.Shared);
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

  private static (int Percent, bool IsOnAc) ReadCore(CancellationToken cancellationToken, WmiProbeProcess probe)
  {
    if (ReadCoreOverrideForTests is { } readOverride)
      return readOverride();

    try
    {
      return ResolveFromSources(TryGetNativeBatteryInfo, () => probe.ReadBattery(cancellationToken));
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

}
