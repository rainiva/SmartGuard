using SmartGuard.Contracts;

namespace SmartGuard.Settings;

public static class LogViewIdleResolver
{
  public static int ResolveFromStatus(StatusPayload status, DateTime nowLocal)
  {
    if (!DateTime.TryParse(status.timestamp, out var publishedAt))
      return status.idleSeconds;

    var elapsed = (int)Math.Max(0, (nowLocal - publishedAt).TotalSeconds);
    return status.idleSeconds + elapsed;
  }
}
