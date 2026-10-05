namespace SmartGuard.Contracts;

/// <summary>
/// 跨进程稳定代码登记表：Engine 写入 status.json 的 <c>guardSource</c> / <c>guardReason</c>
/// 只允许取自本登记表；代码值一经发布不得变更语义。展示文案只在 UI 层
/// （Tray / Settings）映射，Engine 不携带任何中文文案。
/// 读取端遇到未登记代码时按"未知"处理，不得猜测。
/// </summary>
public static class GuardStatusCodes
{
  /// <summary>决策来源代码：manual=人工接管 / automatic=自动策略 / none=不干预。</summary>
  public static class Sources
  {
    public const string Manual = "manual";
    public const string Automatic = "automatic";
    public const string None = "none";
  }

  /// <summary>
  /// 决策与执行原因代码：覆盖 <c>GuardDecisionReason</c> 全部取值，
  /// 以及执行层降级原因（配置不可用 / 被电源生命周期阻止 / 计划探测失败）。
  /// </summary>
  public static class Reasons
  {
    public const string ManualActive = "manual-active";
    public const string PolicySelected = "policy-selected";
    public const string ForegroundMatched = "foreground-matched";
    public const string Paused = "paused";
    public const string ForegroundListEmpty = "foreground-list-empty";
    public const string ForegroundIdentityUnknown = "foreground-identity-unknown";
    public const string WaitingForListedProgram = "waiting-for-listed-program";
    public const string ForegroundObservation = "foreground-observation";
    public const string ForegroundProgramPathsInvalid = "foreground-program-paths-invalid";
    public const string ConfigUnavailable = "config-unavailable";
    public const string BlockedByLifecycle = "blocked-by-lifecycle";
    public const string ProbeFailed = "probe-failed";
  }

  public static bool IsKnownSource(string? code)
    => code is Sources.Manual or Sources.Automatic or Sources.None;

  public static bool IsKnownReason(string? code)
    => code is Reasons.ManualActive
      or Reasons.PolicySelected
      or Reasons.ForegroundMatched
      or Reasons.Paused
      or Reasons.ForegroundListEmpty
      or Reasons.ForegroundIdentityUnknown
      or Reasons.WaitingForListedProgram
      or Reasons.ForegroundObservation
      or Reasons.ForegroundProgramPathsInvalid
      or Reasons.ConfigUnavailable
      or Reasons.BlockedByLifecycle
      or Reasons.ProbeFailed;
}
