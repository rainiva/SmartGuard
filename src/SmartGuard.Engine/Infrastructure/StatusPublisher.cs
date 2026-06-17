using System.Text.Json;
using System.Text.Json.Serialization;
using SmartGuard.Contracts;

namespace SmartGuard.Engine.Infrastructure;

public sealed class StatusPublisher(string statusPath)
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
  };

  public void Publish(StatusPayload payload)
  {
    var dir = Path.GetDirectoryName(statusPath);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    var temp = statusPath + ".tmp";
    File.WriteAllText(temp, JsonSerializer.Serialize(payload, JsonOptions));
    if (File.Exists(statusPath)) File.Delete(statusPath);
    File.Move(temp, statusPath);
  }
}
