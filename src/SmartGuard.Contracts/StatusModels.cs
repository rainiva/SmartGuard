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
  public string? operationId { get; set; }

  /// <summary>
  /// 守护决策来源，取值限 <see cref="GuardStatusCodes.Sources"/>；
  /// null = 旧状态或无决策（UI 不得据此显示确定的正在守护）。
  /// </summary>
  public string? guardSource { get; set; }

  /// <summary>
  /// 守护决策/执行原因，取值限 <see cref="GuardStatusCodes.Reasons"/>；
  /// null = 旧状态或无决策（UI 不得据此显示确定的正在守护）。
  /// </summary>
  public string? guardReason { get; set; }

  /// <summary>
  /// 前台事件监听健康：true=健康 / false=降级为周期补偿；
  /// null = 不适用或未知（如限制关闭、配置不可用或旧状态缺字段）。
  /// </summary>
  public bool? foregroundEventsAvailable { get; set; }
  /// <summary>可选日志健康；旧状态缺字段时未知，不能由日志尾部猜测。</summary>
  public LogHealthStatus? logHealth { get; set; }
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
