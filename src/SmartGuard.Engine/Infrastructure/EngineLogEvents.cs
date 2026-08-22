using SmartGuard.Configuration;
using System.Text.Json;

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
    string? userActionId = null,
    string? outcome = null,
    IReadOnlyDictionary<string, JsonElement>? properties = null)
  {
    var severity = MapSeverity(level);
    var mergedProperties = properties is null
      ? new Dictionary<string, JsonElement>()
      : new Dictionary<string, JsonElement>(properties);
    if (outcome is not null)
    {
      foreach (var property in LogEventOutcome.CreateProperties(outcome))
        mergedProperties[property.Key] = property.Value;
    }
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
      threadId: Environment.CurrentManagedThreadId,
      properties: mergedProperties.Count == 0 ? null : mergedProperties);
  }

  internal static LogEvent CreateLifecycle(
    LogLevel level,
    string eventName,
    string message,
    PowerLifecycleSignal signal,
    string? outcome = null)
    => Create(
      level,
      eventName,
      message,
      outcome: outcome,
      properties: new Dictionary<string, JsonElement>
      {
        ["lifecycleObservedAtUtc"] = JsonSerializer.SerializeToElement(signal.ObservedAtUtc.ToString("O")),
      });

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
