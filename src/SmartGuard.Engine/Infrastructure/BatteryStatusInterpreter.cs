namespace SmartGuard.Engine.Infrastructure;

public static class BatteryStatusInterpreter
{
  public const byte AcLineOffline = 0;
  public const byte AcLineOnline = 1;
  public const byte AcLineUnknown = 255;
  public const byte BatteryPercentUnknown = 255;
  public const byte BatteryFlagNoBattery = 128;

  public static bool? InterpretAcLineStatus(byte acLineStatus) =>
    acLineStatus switch
    {
      AcLineOnline => true,
      AcLineOffline => false,
      _ => null,
    };

  public static int? InterpretBatteryLifePercent(byte batteryLifePercent) =>
    batteryLifePercent == BatteryPercentUnknown ? null : batteryLifePercent;

  public static bool IsNoSystemBattery(byte batteryFlag) =>
    (batteryFlag & BatteryFlagNoBattery) != 0;

  public static bool? InterpretWmiBatteryStatus(int batteryStatus) =>
    batteryStatus switch
    {
      6 or 7 or 8 or 9 => true,
      4 or 5 or 11 => false,
      _ => null,
    };

  public static int AggregateEstimatedChargeRemaining(IEnumerable<(int Percent, uint Weight)> batteries)
  {
    var entries = batteries.Where(b => b.Weight > 0).ToList();
    if (entries.Count == 0) return 100;

    double weighted = 0;
    double totalWeight = 0;
    foreach (var (percent, weight) in entries)
    {
      weighted += percent * weight;
      totalWeight += weight;
    }

    if (totalWeight <= 0) return 100;
    return (int)Math.Round(weighted / totalWeight);
  }

  public static (int Percent, bool IsOnAc) Resolve(
    byte acLineStatus,
    byte batteryLifePercent,
    byte batteryFlag,
    int? wmiPercent,
    bool? wmiOnAc)
  {
    if (IsNoSystemBattery(batteryFlag) && wmiPercent is null)
      return (100, true);

    var onAc = InterpretAcLineStatus(acLineStatus) ?? wmiOnAc ?? true;
    var percent = InterpretBatteryLifePercent(batteryLifePercent) ?? wmiPercent ?? 100;
    return (Math.Clamp(percent, 0, 100), onAc);
  }
}
