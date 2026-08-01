using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SmartGuard.Configuration;

public static class LogJsonlFormatter
{
  private static readonly Regex LinePattern = new(
    @"^\[(\w+)\]\s+(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})\s+\[?(\w+)\]?\s*(.*)$",
    RegexOptions.Compiled);

  private static readonly Regex OpPattern = new(@"op=(\S+)", RegexOptions.Compiled);
  private static readonly Regex TracePattern = new(@"trace=(\S+)", RegexOptions.Compiled);
  private static readonly Regex SpanPattern = new(@"span=(\S+)", RegexOptions.Compiled);
  private static readonly Regex UaPattern = new(@"ua=(\S+)", RegexOptions.Compiled);

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
  };

  public static string FormatLine(LogJsonlEntry entry)
    => JsonSerializer.Serialize(entry, JsonOptions);

  public static bool TryParseLine(string logLine, out LogJsonlEntry? entry)
  {
    entry = null;
    if (string.IsNullOrWhiteSpace(logLine))
      return false;

    var match = LinePattern.Match(logLine);
    if (!match.Success)
      return false;

    var level = match.Groups[1].Value;
    var timestampStr = match.Groups[2].Value;
    var category = match.Groups[3].Value;
    var remainder = match.Groups[4].Value;

    if (!DateTimeOffset.TryParseExact(timestampStr, "yyyy-MM-dd HH:mm:ss",
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var timestamp))
      return false;

    var operationId = ExtractMatchValue(OpPattern, remainder);
    var traceId = ExtractMatchValue(TracePattern, remainder);
    var spanId = ExtractMatchValue(SpanPattern, remainder);
    var userActionId = ExtractMatchValue(UaPattern, remainder);

    var eventNamePart = remainder.Split(' ')[0];
    string? eventName = string.IsNullOrEmpty(eventNamePart) || eventNamePart.Contains('=') ? null : eventNamePart;

    entry = new LogJsonlEntry(
      timestamp, level, category, eventName, remainder,
      operationId, traceId, spanId, userActionId, null, null);
    return true;
  }

  private static string? ExtractMatchValue(Regex pattern, string input)
  {
    var m = pattern.Match(input);
    return m.Success ? m.Groups[1].Value : null;
  }
}
