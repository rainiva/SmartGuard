using SmartGuard.Contracts;

namespace SmartGuard.Tray;

public static class TrayStatusFormatter
{
  public static string FormatTooltip(StatusPayload? status, DateTimeOffset? nowUtc = null)
  {
    if (status is null) return "智能电源守护（等待核心服务）";
    var power = status.isOnAC ? "插电" : "电池";
    var text = $"计划: {status.currentPlan} | {status.batteryPercent}% {power} | 亮度{status.brightness}%";
    if (status.paused) text += " | 已暂停";
    text += FormatGuardClauseWithSeparator(status, " | ", nowUtc);
    text += FormatLogHealthClause(status, nowUtc);
    return text;
  }

  public static string FormatStatusLine(StatusPayload? status, DateTimeOffset? nowUtc = null)
  {
    if (status is null) return "等待核心服务…";
    var power = status.isOnAC ? "插电" : "电池";
    var text = $"计划：{status.currentPlan} | {status.batteryPercent}% {power}";
    if (status.paused) text += " | 已暂停";
    text += FormatGuardClauseWithSeparator(status, " | ", nowUtc);
    text += FormatLogHealthClause(status, nowUtc);
    return text;
  }

  private static string FormatLogHealthClause(StatusPayload status, DateTimeOffset? nowUtc)
    => LogHealthCodes.TrustedCode(status.logHealth, nowUtc ?? DateTimeOffset.UtcNow) switch
    {
      LogHealthCodes.Healthy => string.Empty,
      LogHealthCodes.Degraded => " | 日志降级",
      LogHealthCodes.Failed => " | 日志可能缺失",
      _ => " | 日志状态未知",
    };

  /// <summary>
  /// 守护子句（含分隔符）：只在状态新鲜（<see cref="GuardStatusFreshness"/>，与 Settings 同一上界）
  /// 且携带已登记的 guardSource/guardReason 时输出；状态过期、时间戳缺失/不可信、
  /// 旧状态缺字段或代码未登记时输出空串——不得显示确定的正在守护。
  /// 暂停沿用既有"已暂停"展示（paused 布尔字段），paused 原因不重复输出子句。
  /// </summary>
  private static string FormatGuardClauseWithSeparator(StatusPayload status, string separator, DateTimeOffset? nowUtc)
  {
    var clause = FormatGuardClause(status, nowUtc);
    return string.IsNullOrEmpty(clause) ? string.Empty : separator + clause;
  }

  private static string FormatGuardClause(StatusPayload status, DateTimeOffset? nowUtc)
  {
    if (status.paused) return string.Empty;
    if (!GuardStatusFreshness.IsFresh(status.timestamp, nowUtc ?? DateTimeOffset.UtcNow))
      return string.Empty;
    if (!GuardStatusCodes.IsKnownSource(status.guardSource)
      || !GuardStatusCodes.IsKnownReason(status.guardReason))
      return string.Empty;

    // 确定的守护声明必须与来源代码一致：守护中只认 automatic，临时高性能只认 manual。
    return (status.guardSource, status.guardReason) switch
    {
      (GuardStatusCodes.Sources.Manual, GuardStatusCodes.Reasons.ManualActive) => "临时高性能",
      (GuardStatusCodes.Sources.Automatic, GuardStatusCodes.Reasons.PolicySelected) => "守护中",
      (GuardStatusCodes.Sources.Automatic, GuardStatusCodes.Reasons.ForegroundMatched) => "守护中",
      (_, GuardStatusCodes.Reasons.ForegroundListEmpty) => "添加程序后生效",
      (_, GuardStatusCodes.Reasons.ForegroundIdentityUnknown) => "暂时无法识别前台程序",
      (_, GuardStatusCodes.Reasons.WaitingForListedProgram) => "等待指定程序",
      (_, GuardStatusCodes.Reasons.ForegroundObservation) => "退出观察中",
      (_, GuardStatusCodes.Reasons.ForegroundProgramPathsInvalid) => "名单配置异常",
      (_, GuardStatusCodes.Reasons.ConfigUnavailable) => "配置不可用",
      (_, GuardStatusCodes.Reasons.ProbeFailed) => "状态探测失败",
      (_, GuardStatusCodes.Reasons.BlockedByLifecycle) => "电源恢复暂缓守护",
      _ => string.Empty,
    };
  }
}

public static class NotificationDeduper
{
  public static bool ShouldShow(string? lastEventId, NotificationEvent? evt)
  {
    if (evt is null || string.IsNullOrEmpty(evt.id)) return false;
    return !string.Equals(lastEventId, evt.id, StringComparison.Ordinal);
  }
}
