using System.Text.Json;
using SmartGuard.Contracts;

namespace SmartGuard.Tray;

public sealed class StatusStore(string statusPath)
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
  };

  public StatusPayload? Read()
  {
    if (!File.Exists(statusPath)) return null;
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
