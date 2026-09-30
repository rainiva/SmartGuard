using SmartGuard.Contracts;

namespace SmartGuard.Engine.Domain;

/// <summary>
/// <see cref="GuardDecision"/> 枚举 → 跨进程稳定代码的唯一映射
/// （登记表 = <see cref="GuardStatusCodes"/>）。Engine 只发布登记代码，
/// 不产生任何展示文案；文案映射只允许存在于 UI 层（Tray / Settings）。
/// </summary>
public static class GuardDecisionCodes
{
  public static string ToSourceCode(GuardDecisionSource source) => source switch
  {
    GuardDecisionSource.Manual => GuardStatusCodes.Sources.Manual,
    GuardDecisionSource.Automatic => GuardStatusCodes.Sources.Automatic,
    GuardDecisionSource.None => GuardStatusCodes.Sources.None,
    _ => throw new ArgumentOutOfRangeException(nameof(source), source, "未登记的决策来源"),
  };

  public static string ToReasonCode(GuardDecisionReason reason) => reason switch
  {
    GuardDecisionReason.ManualActive => GuardStatusCodes.Reasons.ManualActive,
    GuardDecisionReason.PolicySelected => GuardStatusCodes.Reasons.PolicySelected,
    GuardDecisionReason.ForegroundMatched => GuardStatusCodes.Reasons.ForegroundMatched,
    GuardDecisionReason.Paused => GuardStatusCodes.Reasons.Paused,
    GuardDecisionReason.ForegroundListEmpty => GuardStatusCodes.Reasons.ForegroundListEmpty,
    GuardDecisionReason.ForegroundIdentityUnknown => GuardStatusCodes.Reasons.ForegroundIdentityUnknown,
    GuardDecisionReason.WaitingForListedProgram => GuardStatusCodes.Reasons.WaitingForListedProgram,
    GuardDecisionReason.ForegroundProgramPathsInvalid => GuardStatusCodes.Reasons.ForegroundProgramPathsInvalid,
    _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "未登记的决策原因"),
  };
}
