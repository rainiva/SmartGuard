using System.Text.Json;
using System.Text.Json.Serialization;

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

public sealed class StatusPayload
{
  public string timestamp { get; set; } = string.Empty;
  public string currentPlan { get; set; } = string.Empty;
  public string? currentPlanGUID { get; set; }
  public string? expectedPlan { get; set; }
  public int idleSeconds { get; set; }
  public bool isOnAC { get; set; }
  public int batteryPercent { get; set; }
  public int brightness { get; set; }
  public bool paused { get; set; }
  public object? lastExternalChange { get; set; }
  public NotificationEvent? notificationEvent { get; set; }
}

public sealed class NotificationEvent
{
  public string id { get; set; } = Guid.NewGuid().ToString("N");
  public string kind { get; set; } = string.Empty;
  public string title { get; set; } = string.Empty;
  public string body { get; set; } = string.Empty;
  public string at { get; set; } = DateTime.Now.ToString("s");
}
