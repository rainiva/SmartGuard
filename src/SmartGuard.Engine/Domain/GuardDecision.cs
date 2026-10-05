namespace SmartGuard.Engine.Domain;

/// <summary>决策来源：人工接管 / 自动策略 / 不干预。</summary>
public enum GuardDecisionSource
{
  Manual,
  Automatic,
  None,
}

/// <summary>决策原因稳定代码；状态字符串映射在后续状态发布任务中登记，代码值保持稳定。</summary>
public enum GuardDecisionReason
{
  /// <summary>人工高性能接管期限有效，沿用人工接管目标。</summary>
  ManualActive,

  /// <summary>前台限制关闭，按空闲/电量/插电链路选择计划。</summary>
  PolicySelected,

  /// <summary>前台命中名单，按空闲/电量/插电链路选择计划。</summary>
  ForegroundMatched,

  /// <summary>用户暂停，不干预（保持既有暂停行为）。</summary>
  Paused,

  /// <summary>前台限制开启且名单为空，不干预。</summary>
  ForegroundListEmpty,

  /// <summary>前台限制开启且前台身份不可识别，不干预。</summary>
  ForegroundIdentityUnknown,

  /// <summary>前台限制开启且前台未命中名单，不干预。</summary>
  WaitingForListedProgram,

  /// <summary>名单存在非法条目、配置不可信，不干预（禁止静默无限制）。</summary>
  ForegroundProgramPathsInvalid,
  /// <summary>离开名单程序后的观察期，不执行自动调整。</summary>
  ForegroundObservation,
}

/// <summary>
/// 统一守护决策值：<see cref="TargetPlanGuid"/> 为 null 明确表示不干预。
/// 唯一产出入口为 <see cref="PolicyEngine.Evaluate"/>；禁止第二套预期计划推导。
/// </summary>
public sealed record GuardDecision(
  Guid? TargetPlanGuid,
  GuardDecisionSource Source,
  GuardDecisionReason Reason,
  int? MatchedRuleIndex = null);

/// <summary>
/// 前台准入结论（只含前台限制规则本身，不含空闲/电源/人工期限）：
/// 由 <see cref="PolicyEngine.EvaluateAdmission"/> 唯一产出，
/// <see cref="PolicyEngine.Evaluate"/> 内部复用同一规则；轻路径与完整轮次共用，不复制名单逻辑。
/// </summary>
public readonly record struct ForegroundAdmission(bool Admitted, GuardDecisionReason Reason, int? MatchedRuleIndex = null);
