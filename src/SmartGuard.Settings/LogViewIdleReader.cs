using System.IO;
using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Contracts;

namespace SmartGuard.Settings;

internal static class LogViewIdleReader
{
  public const int MaxStatusAgeSeconds = 300;
  public const int StaleHintAgeSeconds = 90;

  private static readonly JsonSerializerOptions StatusJsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
  };

  internal static Func<uint>? ReadOverrideForTests;
  internal static Func<uint>? ApiReadOverrideForTests;

  public static int? TryReadSeconds(string installRoot, DateTime? now = null)
    => TryRead(installRoot, now).Seconds;

  public static LogViewIdleReadResult TryRead(string installRoot, DateTime? now = null)
  {
    try
    {
      if (ReadOverrideForTests is not null)
        return new LogViewIdleReadResult((int)ReadOverrideForTests(), StatusMayBeStale: false);

      var nowLocal = now ?? DateTime.Now;
      var apiIdle = ReadApiIdleSeconds();
      var status = TryReadStatus(Path.Combine(installRoot, "SmartGuard.status.json"));
      if (status is null)
        return new LogViewIdleReadResult(apiIdle, StatusMayBeStale: false);

      if (!TryGetPublishedAt(status, out var publishedAt))
        return new LogViewIdleReadResult(
          LogViewIdleDisplayPolicy.Resolve(status.idleSeconds, apiIdle),
          StatusMayBeStale: false);

      var ageSeconds = (int)Math.Max(0, (nowLocal - publishedAt).TotalSeconds);
      if (ageSeconds > MaxStatusAgeSeconds)
        return new LogViewIdleReadResult(apiIdle, StatusMayBeStale: true);

      var extrapolated = LogViewIdleResolver.ResolveFromStatus(status, nowLocal);
      var resolved = LogViewIdleDisplayPolicy.Resolve(extrapolated, apiIdle);
      return new LogViewIdleReadResult(
        resolved,
        StatusMayBeStale: ageSeconds > StaleHintAgeSeconds);
    }
    catch
    {
      return new LogViewIdleReadResult(null, StatusMayBeStale: false);
    }
  }

  private static int ReadApiIdleSeconds()
    => (int)(ApiReadOverrideForTests?.Invoke() ?? IdleDetector.GetIdleSeconds());

  private static bool TryGetPublishedAt(StatusPayload status, out DateTime publishedAt)
    => DateTime.TryParse(status.timestamp, out publishedAt);

  private static StatusPayload? TryReadStatus(string statusPath)
  {
    if (!File.Exists(statusPath))
      return null;

    try
    {
      var json = File.ReadAllText(statusPath);
      return JsonSerializer.Deserialize<StatusPayload>(json, StatusJsonOptions);
    }
    catch
    {
      return null;
    }
  }
}
