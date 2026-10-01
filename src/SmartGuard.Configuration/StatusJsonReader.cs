using System.Text.Json;
using SmartGuard.Contracts;

namespace SmartGuard.Configuration;

public static class StatusJsonReader
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
  };

  public static StatusPayload? TryRead(string statusPath)
  {
    if (!File.Exists(statusPath))
      return null;

    try
    {
      using var stream = new FileStream(statusPath, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete);
      return JsonSerializer.Deserialize<StatusPayload>(stream, JsonOptions);
    }
    catch
    {
      return null;
    }
  }
}
