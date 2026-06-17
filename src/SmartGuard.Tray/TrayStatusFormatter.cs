using SmartGuard.Contracts;

namespace SmartGuard.Tray;

public static class TrayStatusFormatter
{
  public static string FormatTooltip(StatusPayload? status)
  {
    if (status is null) return "智能电源守护（等待核心服务）";
    var power = status.isOnAC ? "插电" : "电池";
    var text = $"计划: {status.currentPlan} | {status.batteryPercent}% {power} | 亮度{status.brightness}%";
    if (status.paused) text += " [已暂停]";
    return text;
  }

  public static string FormatStatusLine(StatusPayload? status)
  {
    if (status is null) return "等待核心服务…";
    var power = status.isOnAC ? "插电" : "电池";
    var text = $"计划：{status.currentPlan} | {status.batteryPercent}% {power}";
    if (status.paused) text += " | 已暂停";
    return text;
  }
}

public static class NotificationDeduper
{
  public static bool ShouldShow(string? lastEventId, NotificationEvent? evt)
  {
    if (evt is null || string.IsNullOrEmpty(evt.id)) return false;
    return !string.Equals(lastEventId, evt.id, StringComparison.Ordinal);
  }
}
