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

  private StatusPayload? _lastPayload;

  public void Publish(StatusPayload payload)
  {
    if (_lastPayload is not null && StatusPayloadEquals(_lastPayload, payload))
      return;

    var dir = Path.GetDirectoryName(statusPath);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    var temp = statusPath + ".tmp";
    File.WriteAllText(temp, JsonSerializer.Serialize(payload, JsonOptions));
    if (File.Exists(statusPath)) File.Delete(statusPath);
    File.Move(temp, statusPath);
    _lastPayload = payload;
  }

  private static bool StatusPayloadEquals(StatusPayload a, StatusPayload b)
  {
    return a.currentPlan == b.currentPlan
      && a.currentPlanGUID == b.currentPlanGUID
      && a.expectedPlan == b.expectedPlan
      && a.idleSeconds == b.idleSeconds
      && a.isOnAC == b.isOnAC
      && a.batteryPercent == b.batteryPercent
      && a.brightness == b.brightness
      && a.paused == b.paused
      && a.notificationEvent?.id == b.notificationEvent?.id;
  }
}
