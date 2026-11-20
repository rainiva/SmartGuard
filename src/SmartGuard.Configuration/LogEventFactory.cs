using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartGuard.Configuration;

public static class LogEventFactory
{
  public static LogEvent Create(
    LogSeverity severity,
    string component,
    string channel,
    string eventName,
    string message,
    LogExceptionInfo? exception = null,
    string? operationId = null,
    string? traceId = null,
    string? spanId = null,
    string? userActionId = null,
    int? processId = null,
    int? threadId = null,
    IReadOnlyDictionary<string, JsonElement>? properties = null)
  {
    if (!LogComponentRegistry.IsRegistered(component))
      throw new ArgumentException($"Unknown component '{component}'.", nameof(component));
    if (!LogChannelRegistry.IsRegistered(channel))
      throw new ArgumentException($"Unknown channel '{channel}'.", nameof(channel));
    if (string.IsNullOrWhiteSpace(eventName) || !EventNameRegistry.IsRegistered(eventName))
      throw new ArgumentException($"Unknown or empty event name '{eventName}'.", nameof(eventName));
    if (string.IsNullOrWhiteSpace(message))
      throw new ArgumentException("Message must not be empty.", nameof(message));

    return new LogEvent(
      LogEvent.CurrentSchemaVersion,
      CreateStructuredEventId(),
      DateTimeOffset.UtcNow,
      severity,
      component,
      channel,
      eventName,
      message,
      exception,
      operationId,
      traceId,
      spanId,
      userActionId,
      processId,
      threadId,
      properties ?? LogEvent.EmptyProperties);
  }

  internal static string CreateStructuredEventId()
    => "e:" + Guid.NewGuid().ToString("N");
}

public static class LogEventId
{
  internal static string ComputeLegacyEventId(
    LogSourceIdentity source,
    long byteOffset,
    string lineText,
    int ordinal)
  {
    var lineHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lineText))).ToLowerInvariant();
    var payload = string.Create(
      CultureInfo.InvariantCulture,
      $"{source.CreatedAtUtc:O}|{source.ContentHashHex}|{source.FileId}|{byteOffset}|{lineHash}|{ordinal}");
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    return "l:" + hash;
  }
}

internal static class LogSeverityParser
{
  internal static LogSeverity Parse(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
      return LogSeverity.Info;

    return value.Trim().ToUpperInvariant() switch
    {
      "TRACE" => LogSeverity.Trace,
      "DEBUG" => LogSeverity.Debug,
      "INFO" => LogSeverity.Info,
      "WARN" or "WARNING" => LogSeverity.Warn,
      "ERROR" => LogSeverity.Error,
      "FATAL" => LogSeverity.Fatal,
      _ when Enum.TryParse<LogSeverity>(value, ignoreCase: true, out var parsed) => parsed,
      _ => LogSeverity.Info,
    };
  }

  internal static string ToToken(LogSeverity severity)
    => severity switch
    {
      LogSeverity.Trace => "TRACE",
      LogSeverity.Debug => "DEBUG",
      LogSeverity.Info => "INFO",
      LogSeverity.Warn => "WARN",
      LogSeverity.Error => "ERROR",
      LogSeverity.Fatal => "FATAL",
      _ => severity.ToString().ToUpperInvariant(),
    };
}
