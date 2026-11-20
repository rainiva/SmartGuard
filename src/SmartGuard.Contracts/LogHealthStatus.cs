using System.Text.Json.Serialization;

namespace SmartGuard.Contracts;

/// <summary>日志自身的观测时间独立于守护事实时间；累计损失不表示当前仍故障。</summary>
public sealed record LogHealthStatus(
  [property: JsonPropertyName("code")] string Code,
  [property: JsonPropertyName("observedAtUtc")] DateTimeOffset ObservedAtUtc,
  [property: JsonPropertyName("primaryWrittenLines")] long PrimaryWrittenLines,
  [property: JsonPropertyName("fallbackWrittenLines")] long FallbackWrittenLines,
  [property: JsonPropertyName("lostLines")] long LostLines,
  [property: JsonPropertyName("droppedNormal")] long DroppedNormal,
  [property: JsonPropertyName("droppedHigh")] long DroppedHigh,
  [property: JsonPropertyName("droppedByBackpressure")] long DroppedByBackpressure);

public static class LogHealthCodes
{
  public const string Healthy = "healthy";
  public const string Degraded = "degraded";
  public const string Failed = "failed";
  public const string Unknown = "unknown";

  public static string TrustedCode(LogHealthStatus? health, DateTimeOffset nowUtc)
  {
    if (health is null || !GuardStatusFreshness.IsFresh(health.ObservedAtUtc.ToString("O"), nowUtc)
      || health.PrimaryWrittenLines < 0 || health.FallbackWrittenLines < 0 || health.LostLines < 0
      || health.DroppedNormal < 0 || health.DroppedHigh < 0 || health.DroppedByBackpressure < 0)
      return Unknown;
    return health.Code is Healthy or Degraded or Failed ? health.Code : Unknown;
  }
}
