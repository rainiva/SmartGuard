using SmartGuard.Contracts;

namespace SmartGuard.Engine.Worker;

public static class GuardianLogMessages
{
  public static string FormatHeartbeat(
    string label,
    string planName,
    int idleSeconds,
    int batteryPercent,
    bool isOnAc,
    bool paused,
    int brightness,
    string? guardReason = null,
    Guid? targetPlanGuid = null,
    Guid? actualPlanGuid = null)
  {
    var pwr = isOnAc ? "插电" : "电池";
    var brightPart = brightness >= 0 ? $"亮度{brightness}%" : "亮度N/A";
    var observation = guardReason switch
    {
      GuardStatusCodes.Reasons.ManualActive => "人工接管有效",
      GuardStatusCodes.Reasons.Paused => "守护已暂停",
      GuardStatusCodes.Reasons.WaitingForListedProgram => "前台未命中，保留当前计划",
      GuardStatusCodes.Reasons.ForegroundIdentityUnknown => "前台身份未知，停止干预",
      GuardStatusCodes.Reasons.ForegroundListEmpty => "名单为空，停止干预",
      GuardStatusCodes.Reasons.ForegroundProgramPathsInvalid => "名单无效，停止干预",
      GuardStatusCodes.Reasons.ConfigUnavailable => "配置不可用，停止干预",
      GuardStatusCodes.Reasons.ProbeFailed => "计划探测失败",
      GuardStatusCodes.Reasons.BlockedByLifecycle => "生命周期阻止策略变更",
      _ => paused ? "守护已暂停" : "仅观测计划",
    };
    if (!paused && (guardReason is GuardStatusCodes.Reasons.PolicySelected or GuardStatusCodes.Reasons.ForegroundMatched or GuardStatusCodes.Reasons.ManualActive)
        && targetPlanGuid is not null && actualPlanGuid == targetPlanGuid)
      observation += "，当前已符合目标";
    return $"{label} (未操作{idleSeconds}秒) | {observation} | {planName} | 电量{batteryPercent}% {pwr} | {brightPart}";
  }

  public static string FormatStatusLabelChange(
    string label,
    int idleSeconds,
    string planName,
    int batteryPercent,
    bool isOnAc,
    int brightness)
  {
    var pwr = isOnAc ? "插电" : "电池";
    var brightPart = brightness >= 0 ? $"亮度{brightness}%" : "亮度WMI不支持";
    return $"状态: {label} (未操作{idleSeconds}秒) | 当前计划 | {planName} | 电量{batteryPercent}% {pwr} | {brightPart}";
  }

  public static string FormatBrightnessChange(int before, int after)
    => $"亮度变化: {before}% -> {after}%";
}
