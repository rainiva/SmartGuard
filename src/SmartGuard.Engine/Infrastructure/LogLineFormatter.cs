namespace SmartGuard.Engine.Infrastructure;

public static class LogLineFormatter
{
  public static string Format(DateTime timestamp, LogLevel level, string message)
  {
    var tag = level switch
    {
      LogLevel.Info => "INFO",
      LogLevel.Warn => "WARN",
      LogLevel.Error => "ERROR",
      LogLevel.Heart => "HEART",
      _ => "INFO",
    };
    return $"[{tag}] {timestamp:yyyy-MM-dd HH:mm:ss} {message}";
  }
}
