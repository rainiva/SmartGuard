using SmartGuard.Configuration;

namespace SmartGuard.Engine.Domain;

public static class PolicyEngine
{
  public static Guid? GetExpectedPlanGuid(int idleSeconds, bool isOnAc, int batteryPercent, GuardConfig config)
  {
    if (config.IsManualHighPerformanceActive(DateTime.Now))
      return config.ActivePlanGuid;

    if (config.Paused) return null;

    if (idleSeconds >= config.PowerSaverThresholdSec)
      return config.PowerSaverPlanGuid;

    if (idleSeconds >= config.BalancedThresholdSec)
      return config.BalancedPlanGuid;

    if (isOnAc || batteryPercent >= config.LowBatteryPercent)
      return config.ActivePlanGuid;

    return config.BalancedPlanGuid;
  }

  public static bool ShouldApplyPowerPlanSwitch(Guid? current, Guid? target)
  {
    if (target is null) return false;
    if (current is null) return true;
    return current.Value != target.Value;
  }

  public static bool IsExternalPlanChange(Guid? previous, Guid? current, bool scriptJustSwitched)
  {
    if (scriptJustSwitched) return false;
    if (previous is null || current is null) return false;
    return previous.Value != current.Value;
  }

  public static string GetPlanDisplayName(
    Guid? planGuid,
    GuardConfig config,
    IReadOnlyDictionary<Guid, string>? systemPlans = null,
    string? preferredName = null)
  {
    if (planGuid is null) return string.Empty;
    if (!string.IsNullOrWhiteSpace(preferredName))
      return preferredName;
    if (systemPlans is not null
        && systemPlans.TryGetValue(planGuid.Value, out var name)
        && !string.IsNullOrWhiteSpace(name))
      return name;
    if (planGuid == config.ActivePlanGuid) return "高性能";
    if (planGuid == config.BalancedPlanGuid) return "平衡";
    if (planGuid == config.PowerSaverPlanGuid) return "节能";
    return planGuid.Value.ToString();
  }

  public static string GetStatusLabel(int idleSeconds, GuardConfig config)
  {
    if (idleSeconds >= config.PowerSaverThresholdSec) return "深度空闲";
    if (idleSeconds >= config.BalancedThresholdSec) return "空闲";
    return "活跃";
  }
}
