namespace SmartGuard.Engine.Worker;

public static class GuardianLogMessages
{
  public static string FormatHeartbeat(
    string label,
    string planName,
    int batteryPercent,
    bool isOnAc,
    bool paused,
    int brightness)
  {
    var pwr = isOnAc ? "插电" : "电池";
    var brightPart = brightness >= 0 ? $"亮度{brightness}%" : "亮度N/A";
    var pause = paused ? " | 已暂停" : string.Empty;
    return $"{label} | 计划正常 | {planName} | 电量{batteryPercent}% {pwr} | {brightPart}{pause}";
  }

  public static string FormatStatusLabelChange(
    string label,
    int idleSeconds,
    string planName,
    int batteryPercent,
    bool isOnAc,
    int brightness)
  {
    var pwr = isOnAc ? "插电" : "电池";
    var brightPart = brightness >= 0 ? $"亮度{brightness}%" : "亮度WMI不支持";
    return $"状态: {label} (空闲{idleSeconds}秒) | 计划正常 | {planName} | 电量{batteryPercent}% {pwr} | {brightPart}";
  }

  public static string FormatBrightnessChange(int before, int after)
    => $"亮度变化: {before}% -> {after}%";
}
