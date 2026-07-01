namespace SmartGuard.Contracts;

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
  public int enginePid { get; set; }
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

public static class NotificationKinds
{
  public const string PlanSwitch = "plan_switch";
  public const string ExternalChange = "external_change";
}
