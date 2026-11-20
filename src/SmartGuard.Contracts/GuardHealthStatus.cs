using System.Text.Json.Serialization;

namespace SmartGuard.Contracts;

/// <summary>
/// 守护健康（GUARD-INV-LIVENESS）：Engine 存活且可守护时，计划读取是否在连续清醒时间内持续失败。
/// 新鲜度跟随所在状态的 timestamp（GuardStatusFreshness）；旧状态缺字段为未知，UI 不据此报警也不宣称健康。
/// </summary>
public sealed record GuardHealthStatus(
  [property: JsonPropertyName("code")] string Code,
  [property: JsonPropertyName("stalledSinceUtc")] DateTimeOffset? StalledSinceUtc,
  [property: JsonPropertyName("lastFailureKind")] string? LastFailureKind);

public static class GuardHealthCodes
{
  public const string Healthy = "healthy";
  public const string Stalled = "stalled";
  public const string NotApplicable = "not-applicable";

  /// <summary>最近一次计划读取失败的稳定分类；展示文案只在 UI 层映射。</summary>
  public static class FailureKinds
  {
    public const string ApiError = "api-error";
    public const string CallPending = "call-pending";
    public const string TimedOut = "timed-out";
    public const string Other = "other";
  }

  /// <summary>只有新鲜状态里登记过的 stalled 才算停工；其余一律不是确定的停工。</summary>
  public static bool IsStalled(StatusPayload? status, DateTimeOffset nowUtc)
    => status?.guardHealth is { Code: Stalled, StalledSinceUtc: not null }
      && GuardStatusFreshness.IsFresh(status.timestamp, nowUtc);
}
