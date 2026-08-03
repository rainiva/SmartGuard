using SmartGuard.Configuration;

namespace SmartGuard.Engine.Infrastructure;

public static class EngineLogEvents
{
  public static LogEvent Create(
    LogLevel level,
    string eventName,
    string message,
    string? operationId = null,
    string? traceId = null,
    string? spanId = null,
    string? userActionId = null)
  {
    var severity = MapSeverity(level);
    return LogEventFactory.Create(
      severity,
      LogComponentRegistry.Engine,
      LogEventChannels.ForEventName(eventName),
      eventName,
      message,
      operationId: operationId,
      traceId: traceId,
      spanId: spanId,
      userActionId: userActionId,
      processId: Environment.ProcessId,
      threadId: Environment.CurrentManagedThreadId);
  }

  internal static LogSeverity MapSeverity(LogLevel level)
    => level switch
    {
      LogLevel.Trace => LogSeverity.Trace,
      LogLevel.Debug => LogSeverity.Debug,
      LogLevel.Info or LogLevel.Heart => LogSeverity.Info,
      LogLevel.Warn => LogSeverity.Warn,
      LogLevel.Error => LogSeverity.Error,
      LogLevel.Critical => LogSeverity.Fatal,
      _ => LogSeverity.Info,
    };
}
