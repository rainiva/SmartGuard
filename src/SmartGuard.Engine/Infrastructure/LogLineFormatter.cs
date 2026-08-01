namespace SmartGuard.Engine.Infrastructure;

public static class LogLineFormatter
{
  public static string Format(DateTime timestamp, LogLevel level, string message)
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
    return $"[{tag}] {timestamp:yyyy-MM-dd HH:mm:ss} {message}";
  }
}
