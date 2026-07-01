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
      var json = File.ReadAllText(statusPath);
      return JsonSerializer.Deserialize<StatusPayload>(json, JsonOptions);
    }
    catch
    {
      return null;
    }
  }
}
