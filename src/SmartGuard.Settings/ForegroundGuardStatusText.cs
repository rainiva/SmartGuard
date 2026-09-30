using SmartGuard.Contracts;

namespace SmartGuard.Settings;

/// <summary>
/// Settings 侧自动守护状态文案：guardSource/guardReason 稳定代码（真源 =
/// <see cref="GuardStatusCodes"/>）→ 展示文案；文案只在 UI 层维护，Engine 不携带。
/// 状态缺失、时间戳过期或代码未登记一律显示"状态未知"，不得显示确定的正在守护；
/// 读取只经 StatusJsonReader 共享链，不建立第二套状态解析。
/// </summary>
internal static class ForegroundGuardStatusText
{
  /// <summary>状态新鲜度保守上界：Engine 无变化时最长 60 秒刷新一次，连续 3 个周期未见刷新视为过期。</summary>
  internal static readonly TimeSpan MaxStatusAge = TimeSpan.FromMinutes(3);

  internal const string UnknownText = "状态未知";

  public static string Describe(StatusPayload? status, DateTimeOffset? nowUtc = null)
  {
    if (status is null || !IsFresh(status.timestamp, nowUtc ?? DateTimeOffset.UtcNow))
      return UnknownText;

    if (status.paused)
      return "已暂停";

    if (!GuardStatusCodes.IsKnownSource(status.guardSource)
      || !GuardStatusCodes.IsKnownReason(status.guardReason))
      return UnknownText;

    return (status.guardSource, status.guardReason) switch
    {
      (GuardStatusCodes.Sources.Manual, GuardStatusCodes.Reasons.ManualActive) => "临时高性能",
      (GuardStatusCodes.Sources.Automatic, GuardStatusCodes.Reasons.PolicySelected) => "守护中",
      (GuardStatusCodes.Sources.Automatic, GuardStatusCodes.Reasons.ForegroundMatched) => "守护中",
      (_, GuardStatusCodes.Reasons.ForegroundListEmpty) => "添加程序后生效",
      (_, GuardStatusCodes.Reasons.ForegroundIdentityUnknown) => "暂时无法识别前台程序",
      (_, GuardStatusCodes.Reasons.WaitingForListedProgram) => "等待指定程序",
      (_, GuardStatusCodes.Reasons.ForegroundProgramPathsInvalid) => "名单配置异常",
      (_, GuardStatusCodes.Reasons.ConfigUnavailable) => "配置不可用",
      (_, GuardStatusCodes.Reasons.ProbeFailed) => "状态探测失败",
      (_, GuardStatusCodes.Reasons.BlockedByLifecycle) => "电源恢复暂缓守护",
      _ => UnknownText,
    };
  }

  /// <summary>
  /// timestamp 为 UTC（Engine 以 "O" 写入）。过期或来自未来超过上界（时钟回拨保护）均视为不可信。
  /// </summary>
  private static bool IsFresh(string timestamp, DateTimeOffset nowUtc)
  {
    if (!DateTimeOffset.TryParse(timestamp, out var written))
      return false;

    var age = nowUtc - written;
    return age <= MaxStatusAge && age >= -MaxStatusAge;
  }
}
