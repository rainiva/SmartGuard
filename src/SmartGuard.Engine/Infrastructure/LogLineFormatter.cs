namespace SmartGuard.Engine.Infrastructure;

public static class LogLineFormatter
{
  public static string Format(
    DateTime timestamp,
    LogLevel level,
    string message,
    string? traceId = null,
    string? spanId = null,
    string? operationId = null,
    string? userActionId = null)
  {
    var tag = level switch
    {
      LogLevel.Trace => "TRACE",
      LogLevel.Debug => "DEBUG",
      LogLevel.Info => "INFO",
      LogLevel.Warn => "WARN",
      LogLevel.Error => "ERROR",
      LogLevel.Critical => "CRITICAL",
      LogLevel.Heart => "HEART",
      _ => "INFO",
    };
    var baseLine = $"[{tag}] {timestamp:yyyy-MM-dd HH:mm:ss} {message}";
    var suffix = BuildContextSuffix(traceId, spanId, operationId, userActionId);
    return suffix.Length == 0 ? baseLine : baseLine + " " + suffix;
  }

  internal static string BuildContextSuffix(
    string? traceId, string? spanId, string? operationId, string? userActionId)
  {
    if (string.IsNullOrEmpty(traceId) && string.IsNullOrEmpty(spanId)
        && string.IsNullOrEmpty(operationId) && string.IsNullOrEmpty(userActionId))
      return string.Empty;
    var parts = new System.Text.StringBuilder();
    if (!string.IsNullOrEmpty(traceId)) parts.Append("trace=").Append(traceId).Append(' ');
    if (!string.IsNullOrEmpty(spanId)) parts.Append("span=").Append(spanId).Append(' ');
    if (!string.IsNullOrEmpty(operationId)) parts.Append("op=").Append(operationId).Append(' ');
    if (!string.IsNullOrEmpty(userActionId)) parts.Append("ua=").Append(userActionId).Append(' ');
    if (parts.Length == 0) return string.Empty;
    // 去掉末尾空格
    return parts.ToString(0, parts.Length - 1);
  }
}
