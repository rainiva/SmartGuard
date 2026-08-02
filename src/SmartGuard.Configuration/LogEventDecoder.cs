using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartGuard.Configuration;

public static class LogEventDecoder
{
  private static readonly Regex LegacyLinePattern = new(
    @"^\[(?<level>\w+)\]\s+(?<timestamp>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})\s+\[?(?<category>\w+)\]?\s*(?<remainder>.*)$",
    RegexOptions.Compiled);

  private static readonly Regex OpPattern = new(@"op=(?<value>\S+)", RegexOptions.Compiled);
  private static readonly Regex TracePattern = new(@"trace=(?<value>\S+)", RegexOptions.Compiled);
  private static readonly Regex SpanPattern = new(@"span=(?<value>\S+)", RegexOptions.Compiled);
  private static readonly Regex UaPattern = new(@"ua=(?<value>\S+)", RegexOptions.Compiled);

  public static LogEvent Decode(LogPhysicalLine line)
  {
    var text = line.Text;
    if (string.IsNullOrWhiteSpace(text))
      return CreateCompatibilityEvent(line, string.Empty);

    var trimmed = text.TrimStart();
    if (trimmed.StartsWith('{'))
    {
      try
      {
        using var document = JsonDocument.Parse(trimmed);
        if (LogJsonlFormatter.TryReadStructuredEvent(document.RootElement, out var structured))
          return structured;
      }
      catch (JsonException)
      {
        // fall through to compatibility decode
      }
    }

    if (TryDecodeLegacyLine(line, out var legacy))
      return legacy;

    return CreateCompatibilityEvent(line, text);
  }

  private static bool TryDecodeLegacyLine(LogPhysicalLine line, out LogEvent logEvent)
  {
    logEvent = null!;
    var match = LegacyLinePattern.Match(line.Text);
    if (!match.Success)
      return false;

    if (!DateTimeOffset.TryParseExact(
          match.Groups["timestamp"].Value,
          "yyyy-MM-dd HH:mm:ss",
          CultureInfo.InvariantCulture,
          DateTimeStyles.AssumeLocal,
          out var timestamp))
      return false;

    var category = match.Groups["category"].Value;
    var remainder = match.Groups["remainder"].Value;
    var component = LogComponentRegistry.IsRegistered(category)
      ? category
      : LogComponentRegistry.Legacy;

    var eventNamePart = remainder.Split(' ')[0];
    string? eventName = !string.IsNullOrEmpty(eventNamePart)
                        && !eventNamePart.Contains('=')
                        && EventNameRegistry.IsRegistered(eventNamePart)
      ? eventNamePart
      : null;

    var message = remainder;
    if (eventName is not null)
    {
      if (remainder.StartsWith(eventName + " ", StringComparison.Ordinal))
        message = remainder[(eventName.Length + 1)..];
      else if (remainder.Equals(eventName, StringComparison.Ordinal))
        message = string.Empty;
    }

    logEvent = new LogEvent(
      LogEvent.CurrentSchemaVersion,
      LogEventId.ComputeLegacyEventId(line.Source, line.ByteOffset, line.Text, line.Ordinal),
      timestamp,
      LogSeverityParser.Parse(match.Groups["level"].Value),
      component,
      LogChannelRegistry.Compatibility,
      eventName,
      message,
      Exception: null,
      ExtractMatchValue(OpPattern, remainder),
      ExtractMatchValue(TracePattern, remainder),
      ExtractMatchValue(SpanPattern, remainder),
      ExtractMatchValue(UaPattern, remainder),
      ProcessId: null,
      ThreadId: null,
      LogEvent.EmptyProperties);
    return true;
  }

  private static LogEvent CreateCompatibilityEvent(LogPhysicalLine line, string message)
    => new(
      LogEvent.CurrentSchemaVersion,
      LogEventId.ComputeLegacyEventId(line.Source, line.ByteOffset, line.Text, line.Ordinal),
      DateTimeOffset.UtcNow,
      LogSeverity.Info,
      LogComponentRegistry.Legacy,
      LogChannelRegistry.Compatibility,
      EventName: null,
      string.IsNullOrWhiteSpace(message) ? line.Text : message,
      Exception: null,
      OperationId: null,
      TraceId: null,
      SpanId: null,
      UserActionId: null,
      ProcessId: null,
      ThreadId: null,
      LogEvent.EmptyProperties);

  private static string? ExtractMatchValue(Regex pattern, string input)
  {
    var match = pattern.Match(input);
    return match.Success ? match.Groups["value"].Value : null;
  }
}
