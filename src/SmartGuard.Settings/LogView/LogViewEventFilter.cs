using System.Globalization;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.LogView;

internal static class LogViewEventFilter
{
    internal static bool Matches(LogEvent logEvent, LogViewFilterState state)
    {
        var line = GetDisplayLine(logEvent);
        var searchComparison = state.SearchCaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (!LogLineTagParser.TryParse(line, out var tag, out _))
        {
            if (state.TimeRange != LogViewTimeRange.All || state.ActiveTagFilters.Count > 0)
                return false;

            var rawLine = FormatFilteredLine(logEvent);
            if (!string.IsNullOrEmpty(state.SearchKeyword) &&
                !rawLine.Contains(state.SearchKeyword, searchComparison))
                return false;

            return true;
        }

        if (state.ActiveTagFilters.Count > 0 && !MatchesTagFilter(tag, state.ActiveTagFilters))
            return false;

        if (!string.IsNullOrEmpty(state.SearchKeyword) &&
            !line.Contains(state.SearchKeyword, searchComparison))
            return false;

        if (state.TimeRange != LogViewTimeRange.All)
        {
            if (!LogLineTimestampParser.TryParseTimestamp(line, out var timestamp))
                return false;

            var now = state.NowProvider?.Invoke() ?? DateTime.Now;
            if (!LogViewTimeRangeFilter.IsWithinRange(
                    state.TimeRange, timestamp, now, state.CustomRangeStart, state.CustomRangeEnd))
                return false;
        }

        if (!string.IsNullOrEmpty(state.OperationIdFilter) &&
            !line.Contains("op=" + state.OperationIdFilter, searchComparison))
            return false;

        if (!string.IsNullOrEmpty(state.EventNameFilter) &&
            !line.Contains(state.EventNameFilter, searchComparison))
            return false;

        if (state.ProcessIdFilter is int processId && logEvent.ProcessId != processId)
            return false;

        if (state.ThreadIdFilter is int threadId && logEvent.ThreadId != threadId)
            return false;

        return true;
    }

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

    private static bool MatchesTagFilter(string tag, IReadOnlyList<string> activeTags)
    {
        var active = activeTags.Select(v => v.ToUpperInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalized = tag.ToUpperInvariant();
        if (active.Contains(normalized))
            return true;
        return normalized == "\u76D1\u63A7\u4E2D" && active.Contains("HEART");
    }
}
