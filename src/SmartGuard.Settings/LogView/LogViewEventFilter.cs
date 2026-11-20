using System.Globalization;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.LogView;

internal static class LogViewEventFilter
{
    internal static bool Matches(LogEvent logEvent, LogViewFilterState state)
        => state.Matches(logEvent);

    internal static IReadOnlyList<LogEvent> FilterEvents(IEnumerable<LogEvent> events, LogViewFilterState state)
        => events.Where(logEvent => Matches(logEvent, state)).ToList();

    internal static IReadOnlyList<string> FilterLines(IEnumerable<LogEvent> events, LogViewFilterState state)
        => FilterEvents(events, state).Select(FormatFilteredLine).ToList();

    internal static string GetDisplayLine(LogEvent logEvent)
    {
        if (IsRawCompatibilityEvent(logEvent))
            return $"[RAW] {logEvent.Message}";

        if (ShouldFormatLegacyMessage(logEvent))
        {
            if (logEvent.Message.TrimStart().StartsWith('['))
                return LogLineDisplayFormatter.FormatLine(logEvent.Message);

            if (logEvent.Message.Contains("2026-", StringComparison.Ordinal)
                || logEvent.Message.Contains("2025-", StringComparison.Ordinal))
                return LogLineDisplayFormatter.FormatLine(logEvent.Message);

            var token = LogSeverityParser.ToToken(logEvent.Severity);
            var timestamp = logEvent.Timestamp.LocalDateTime.ToString(
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            return $"[{token}] {timestamp} {BuildLegacyPayload(logEvent)}".TrimEnd();
        }

        if (logEvent.Component == LogComponentRegistry.Legacy
            && logEvent.Channel == LogChannelRegistry.Compatibility)
        {
            var token = LogSeverityParser.ToToken(logEvent.Severity);
            var timestamp = logEvent.Timestamp.LocalDateTime.ToString(
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var payload = BuildLegacyPayload(logEvent);
            if (!string.IsNullOrWhiteSpace(payload))
                return $"[{token}] {timestamp} {payload}".TrimEnd();
        }

        return LogEventDisplayFormatter.FormatDisplayLine(logEvent);
    }

    private static string FormatFilteredLine(LogEvent logEvent)
    {
        var line = GetDisplayLine(logEvent);
        if (LogLineTagParser.TryParse(line, out _, out _))
            return line;

        return line.StartsWith("[RAW] ", StringComparison.Ordinal) ? line : $"[RAW] {line}";
    }

    private static bool IsRawCompatibilityEvent(LogEvent logEvent)
        => logEvent.Component == LogComponentRegistry.Legacy
           && logEvent.Channel == LogChannelRegistry.Compatibility
           && string.Equals(logEvent.EventName, "raw", StringComparison.Ordinal);

    private static string BuildLegacyPayload(LogEvent logEvent)
    {
        if (!string.IsNullOrWhiteSpace(logEvent.Message))
        {
            if (logEvent.EventName is not null
                && !logEvent.Message.StartsWith(logEvent.EventName, StringComparison.Ordinal))
                return $"{logEvent.EventName} {logEvent.Message}".Trim();

            return logEvent.Message;
        }

        return logEvent.EventName ?? string.Empty;
    }

    private static bool ShouldFormatLegacyMessage(LogEvent logEvent)
        => logEvent.Component == LogComponentRegistry.Legacy
           && logEvent.Channel == LogChannelRegistry.Compatibility
           && !string.IsNullOrWhiteSpace(logEvent.Message)
           && !logEvent.Message.TrimStart().StartsWith('{');

}