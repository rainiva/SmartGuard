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
    var pwr = isOnAc ? "已接通电源" : "使用电池";
    var brightPart = brightness >= 0 ? $"亮度{brightness}%" : "无法读取亮度";
    var observation = guardReason switch
    {
      GuardStatusCodes.Reasons.ManualActive => "正在使用手动选择的高性能计划",
      GuardStatusCodes.Reasons.Paused => "守护已暂停",
      GuardStatusCodes.Reasons.WaitingForListedProgram => "当前未使用名单中的程序，保留当前计划",
      GuardStatusCodes.Reasons.ForegroundObservation => "正在观察程序切换，保留当前计划",
      GuardStatusCodes.Reasons.ForegroundIdentityUnknown => "暂时无法识别当前程序，保留当前计划",
      GuardStatusCodes.Reasons.ForegroundListEmpty => "尚未添加名单程序，保留当前计划",
      GuardStatusCodes.Reasons.ForegroundProgramPathsInvalid => "程序名单配置有误，保留当前计划",
      GuardStatusCodes.Reasons.ConfigUnavailable => "无法读取设置，暂不自动调整",
      GuardStatusCodes.Reasons.ProbeFailed => "无法读取当前电源计划",
      GuardStatusCodes.Reasons.BlockedByLifecycle => "电脑正在休眠或恢复，暂不自动调整",
      _ => paused ? "守护已暂停" : "仅查看当前计划",
    };
    if (!paused && (guardReason is GuardStatusCodes.Reasons.PolicySelected or GuardStatusCodes.Reasons.ForegroundMatched or GuardStatusCodes.Reasons.ManualActive)
        && targetPlanGuid is not null && actualPlanGuid == targetPlanGuid)
      observation += "，当前计划符合设置";
    return $"运行情况：{label}，{idleSeconds}秒未操作。当前电源计划：{planName}。{observation}。电量{batteryPercent}%，{pwr}，{brightPart}。";
  }

  public static string FormatStatusLabelChange(
    string label,
    int idleSeconds,
    string planName,
    int batteryPercent,
    bool isOnAc,
    int brightness)
  {
    var pwr = isOnAc ? "已接通电源" : "使用电池";
    var brightPart = brightness >= 0 ? $"亮度{brightness}%" : "无法读取亮度";
    return $"使用状态变为：{label}，{idleSeconds}秒未操作。当前电源计划：{planName}。电量{batteryPercent}%，{pwr}，{brightPart}。";
  }

  public static string FormatBrightnessChange(int before, int after)
    => $"屏幕亮度从{before}%变为{after}%。";
}
