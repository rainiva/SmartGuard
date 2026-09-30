using SmartGuard.Configuration;

namespace SmartGuard.Engine.Domain;

public static class PolicyEngine
{
  /// <summary>
  /// 统一自动决策唯一入口，按优先级矩阵判定：
  /// 手动临时高性能有效（<paramref name="nowLocal"/> 为显式本地墙钟，人工期限保持本地时间格式）
  /// → 用户暂停 → 前台限制准入（空名单 / 身份不可识别 / 未命中 / 名单非法 → 不干预，
  /// 名单非法禁止静默无限制）→ 既有空闲/电量/插电策略链。
  /// 目标计划为 null 明确表示不干预；路径规范化与校验共用 <see cref="GuardConfigValidator"/> 单源。
  /// </summary>
  public static GuardDecision Evaluate(
    int idleSeconds,
    bool isOnAc,
    int batteryPercent,
    ForegroundObservation foreground,
    GuardConfig config,
    DateTime nowLocal)
  {
    if (config.IsManualHighPerformanceActive(nowLocal))
      return new GuardDecision(config.ActivePlanGuid, GuardDecisionSource.Manual, GuardDecisionReason.ManualActive);

    if (config.Paused)
      return new GuardDecision(null, GuardDecisionSource.None, GuardDecisionReason.Paused);

    var restrictionEnabled = config.ForegroundRestrictionEnabled;
    if (restrictionEnabled)
    {
      var admission = EvaluateAdmission(foreground, config);
      if (!admission.Admitted)
        return new GuardDecision(null, GuardDecisionSource.None, admission.Reason);
    }

    return new GuardDecision(
      SelectPlanByPowerState(idleSeconds, isOnAc, batteryPercent, config),
      GuardDecisionSource.Automatic,
      restrictionEnabled ? GuardDecisionReason.ForegroundMatched : GuardDecisionReason.PolicySelected);
  }

  /// <summary>
  /// 前台准入子规则唯一产出：限制关闭 → 准入（策略链照常）；限制开启时按
  /// 空名单 / 名单非法 / 身份不可识别 / 未命中 → 不准入，命中 → 准入。
  /// <see cref="Evaluate"/> 内部复用同一实现；事件轻路径与周期完整轮次共用，不复制名单逻辑。
  /// </summary>
  public static ForegroundAdmission EvaluateAdmission(ForegroundObservation foreground, GuardConfig config)
  {
    if (!config.ForegroundRestrictionEnabled)
      return new ForegroundAdmission(true, GuardDecisionReason.PolicySelected);

    var storedPaths = config.ForegroundProgramPaths;
    if (storedPaths is null || storedPaths.Count == 0)
      return new ForegroundAdmission(false, GuardDecisionReason.ForegroundListEmpty);

    var normalizedStored = new List<string>(storedPaths.Count);
    foreach (var raw in storedPaths)
    {
      if (!GuardConfigValidator.TryNormalizeProgramPath(raw, out var normalized))
        return new ForegroundAdmission(false, GuardDecisionReason.ForegroundProgramPathsInvalid);
      normalizedStored.Add(normalized);
    }

    if (!foreground.IdentificationSucceeded)
      return new ForegroundAdmission(false, GuardDecisionReason.ForegroundIdentityUnknown);

    var matched = foreground.ExecutablePath is { } observedPath
      && GuardConfigValidator.TryNormalizeProgramPath(observedPath, out var observedNormalized)
      && normalizedStored.Exists(entry =>
        string.Equals(entry, observedNormalized, StringComparison.OrdinalIgnoreCase));

    return matched
      ? new ForegroundAdmission(true, GuardDecisionReason.ForegroundMatched)
      : new ForegroundAdmission(false, GuardDecisionReason.WaitingForListedProgram);
  }

  private static Guid? SelectPlanByPowerState(int idleSeconds, bool isOnAc, int batteryPercent, GuardConfig config)
  {
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

  public static bool IsExternalPlanChange(
    Guid? previous,
    Guid? current,
    bool scriptJustSwitched,
    Guid? expected = null)
  {
    if (scriptJustSwitched) return false;
    if (previous is null || current is null) return false;
    if (previous.Value == current.Value) return false;
    // Tray / owned switches that land on the policy-expected plan are not external.
    if (expected is not null && current.Value == expected.Value) return false;
    return true;
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
    if (planGuid == config.ActivePlanGuid) return PowerPlanCatalogProvider.HighPerformanceDisplayName;
    if (planGuid == config.BalancedPlanGuid) return PowerPlanCatalogProvider.BalancedDisplayName;
    if (planGuid == config.PowerSaverPlanGuid) return PowerPlanCatalogProvider.PowerSaverDisplayName;
    return planGuid.Value.ToString();
  }

  public static string GetStatusLabel(int idleSeconds, GuardConfig config)
  {
    if (idleSeconds >= config.PowerSaverThresholdSec) return "深度空闲";
    if (idleSeconds >= config.BalancedThresholdSec) return "空闲";
    return "活跃";
  }
}
