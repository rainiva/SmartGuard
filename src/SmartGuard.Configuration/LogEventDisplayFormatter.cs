using System.Globalization;
using System.Text;

namespace SmartGuard.Configuration;

public static class LogEventDisplayFormatter
{
  public static string FormatDisplayLine(LogEvent logEvent)
  {
    var level = LogSeverityParser.ToToken(logEvent.Severity);
    var timestamp = logEvent.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    var builder = new StringBuilder();
    builder.Append('[').Append(level).Append("] ");
    builder.Append(timestamp).Append(" [").Append(logEvent.Component).Append("] ");

    if (!string.IsNullOrEmpty(logEvent.EventName))
      builder.Append(logEvent.EventName).Append(' ');

    builder.Append(logEvent.Message);

    AppendContextSuffix(builder, "op", logEvent.OperationId);
    AppendContextSuffix(builder, "trace", logEvent.TraceId);
    AppendContextSuffix(builder, "span", logEvent.SpanId);
    AppendContextSuffix(builder, "ua", logEvent.UserActionId);

    return builder.ToString().TrimEnd();
  }

  public static string FormatLineForUi(string rawLine, LogSourceIdentity? source = null, long byteOffset = 0, int ordinal = 0)
  {
    if (string.IsNullOrEmpty(rawLine))
      return rawLine;

    var trimmed = rawLine.TrimStart();
    if (!trimmed.StartsWith('{'))
      return rawLine;

    var identity = source ?? LogSourceIdentity.FromContentPrefix(DateTimeOffset.UnixEpoch, "ui-display");
    var decoded = LogEventDecoder.Decode(new LogPhysicalLine(identity, byteOffset, rawLine, true, ordinal));
    return FormatDisplayLine(decoded);
  }

  private static void AppendContextSuffix(StringBuilder builder, string key, string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
      return;

    builder.Append(' ').Append(key).Append('=').Append(value);
  }
}
