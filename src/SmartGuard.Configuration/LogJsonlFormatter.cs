using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SmartGuard.Configuration;

public static class LogJsonlFormatter
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
  };

  public static string FormatLine(LogEvent logEvent)
    => JsonSerializer.Serialize(ToPayload(logEvent), JsonOptions);

  public static bool TryParseLine(string logLine, out LogEvent? logEvent)
    => TryParseEvent(logLine, out logEvent);

  public static bool TryParseEvent(string logLine, out LogEvent? logEvent)
  {
    logEvent = null;
    if (string.IsNullOrWhiteSpace(logLine))
      return false;

    var trimmed = logLine.TrimStart();
    if (!trimmed.StartsWith('{'))
      return false;

    try
    {
      using var document = JsonDocument.Parse(trimmed);
      if (!TryReadStructuredEvent(document.RootElement, out var parsed))
        return false;

      logEvent = parsed;
      return true;
    }
    catch (JsonException)
    {
      return false;
    }
  }

  internal static bool TryReadStructuredEvent(JsonElement root, out LogEvent logEvent)
  {
    logEvent = null!;
    if (root.ValueKind != JsonValueKind.Object)
      return false;

    if (!root.TryGetProperty("schemaVersion", out var schemaVersionElement)
        || schemaVersionElement.ValueKind != JsonValueKind.Number
        || schemaVersionElement.GetInt32() != LogEvent.CurrentSchemaVersion)
      return false;

    if (!root.TryGetProperty("eventId", out var eventIdElement)
        || eventIdElement.ValueKind != JsonValueKind.String)
      return false;

    if (!root.TryGetProperty("timestamp", out var timestampElement)
        || timestampElement.ValueKind != JsonValueKind.String
        || !DateTimeOffset.TryParse(timestampElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
      return false;

    if (!root.TryGetProperty("severity", out var severityElement)
        || severityElement.ValueKind != JsonValueKind.String)
      return false;

    if (!root.TryGetProperty("component", out var componentElement)
        || componentElement.ValueKind != JsonValueKind.String)
      return false;

    if (!root.TryGetProperty("channel", out var channelElement)
        || channelElement.ValueKind != JsonValueKind.String)
      return false;

    if (!root.TryGetProperty("message", out var messageElement)
        || messageElement.ValueKind != JsonValueKind.String)
      return false;

    var properties = ReadProperties(root);
    logEvent = new LogEvent(
      LogEvent.CurrentSchemaVersion,
      eventIdElement.GetString()!,
      timestamp.ToUniversalTime(),
      LogSeverityParser.Parse(severityElement.GetString()),
      componentElement.GetString()!,
      channelElement.GetString()!,
      ReadOptionalString(root, "eventName"),
      messageElement.GetString()!,
      ReadException(root),
      ReadOptionalString(root, "operationId"),
      ReadOptionalString(root, "traceId"),
      ReadOptionalString(root, "spanId"),
      ReadOptionalString(root, "userActionId"),
      ReadOptionalInt(root, "processId"),
      ReadOptionalInt(root, "threadId"),
      properties);
    return true;
  }

  private static object ToPayload(LogEvent logEvent)
  {
    var payload = new Dictionary<string, object?>
    {
      ["schemaVersion"] = logEvent.SchemaVersion,
      ["eventId"] = logEvent.EventId,
      ["timestamp"] = logEvent.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
      ["severity"] = logEvent.Severity.ToString(),
      ["component"] = logEvent.Component,
      ["channel"] = logEvent.Channel,
      ["message"] = logEvent.Message,
    };

    if (logEvent.EventName is not null)
      payload["eventName"] = logEvent.EventName;
    if (logEvent.Exception is not null)
      payload["exception"] = ToExceptionPayload(logEvent.Exception);
    if (logEvent.OperationId is not null)
      payload["operationId"] = logEvent.OperationId;
    if (logEvent.TraceId is not null)
      payload["traceId"] = logEvent.TraceId;
    if (logEvent.SpanId is not null)
      payload["spanId"] = logEvent.SpanId;
    if (logEvent.UserActionId is not null)
      payload["userActionId"] = logEvent.UserActionId;
    if (logEvent.ProcessId is not null)
      payload["processId"] = logEvent.ProcessId;
    if (logEvent.ThreadId is not null)
      payload["threadId"] = logEvent.ThreadId;
    if (logEvent.Properties.Count > 0)
      payload["properties"] = logEvent.Properties;

    return payload;
  }

  private static Dictionary<string, object?> ToExceptionPayload(LogExceptionInfo exception)
  {
    var payload = new Dictionary<string, object?>
    {
      ["type"] = exception.Type,
      ["message"] = exception.Message,
    };
    if (exception.StackTrace is not null)
      payload["stackTrace"] = exception.StackTrace;
    if (exception.Inner is not null)
      payload["inner"] = ToExceptionPayload(exception.Inner);
    return payload;
  }

  private static LogExceptionInfo? ReadException(JsonElement root)
  {
    if (!root.TryGetProperty("exception", out var exceptionElement)
        || exceptionElement.ValueKind != JsonValueKind.Object)
      return null;

    return ReadExceptionNode(exceptionElement);
  }

  private static LogExceptionInfo ReadExceptionNode(JsonElement node)
  {
    var type = node.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
      ? typeElement.GetString()!
      : "System.Exception";
    var message = node.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
      ? messageElement.GetString()!
      : string.Empty;
    string? stackTrace = node.TryGetProperty("stackTrace", out var stackElement) && stackElement.ValueKind == JsonValueKind.String
      ? stackElement.GetString()
      : null;
    LogExceptionInfo? inner = node.TryGetProperty("inner", out var innerElement) && innerElement.ValueKind == JsonValueKind.Object
      ? ReadExceptionNode(innerElement)
      : null;

    return new LogExceptionInfo(type, message, stackTrace, inner);
  }

  private static IReadOnlyDictionary<string, JsonElement> ReadProperties(JsonElement root)
  {
    if (!root.TryGetProperty("properties", out var propertiesElement)
        || propertiesElement.ValueKind != JsonValueKind.Object)
      return LogEvent.EmptyProperties;

    return propertiesElement.EnumerateObject()
      .ToDictionary(property => property.Name, property => property.Value.Clone());
  }

  private static string? ReadOptionalString(JsonElement root, string propertyName)
  {
    if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
      return null;
    return element.GetString();
  }

  private static int? ReadOptionalInt(JsonElement root, string propertyName)
  {
    if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Number)
      return null;
    return element.GetInt32();
  }
}
