using System.IO;
using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Contracts;

namespace SmartGuard.Settings;

internal static class LogViewIdleReader
{
  private static readonly JsonSerializerOptions StatusJsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
  };

  internal static Func<uint>? ReadOverrideForTests;
  internal static Func<uint>? ApiReadOverrideForTests;

  public static int? TryReadSeconds(string installRoot, DateTime? now = null)
  {
    try
    {
      if (ReadOverrideForTests is not null)
        return (int)ReadOverrideForTests();

      var nowLocal = now ?? DateTime.Now;
      var status = TryReadStatus(Path.Combine(installRoot, "SmartGuard.status.json"));
      if (status is not null)
        return LogViewIdleResolver.ResolveFromStatus(status, nowLocal);

      return (int)(ApiReadOverrideForTests?.Invoke() ?? IdleDetector.GetIdleSeconds());
    }
    catch
    {
      return null;
    }
  }

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
