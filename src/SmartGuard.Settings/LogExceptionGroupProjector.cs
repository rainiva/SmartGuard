using SmartGuard.Configuration;

namespace SmartGuard.Settings;

public static class LogExceptionGroupProjector
{
    public static IReadOnlyList<LogExceptionGroup> BuildGroups(IReadOnlyList<LogEvent> events)
    {
        var groups = new List<LogExceptionGroup>();
        LogExceptionGroup? activeGroup = null;

        foreach (var logEvent in events)
        {
            if (IsLegacyStackContinuation(logEvent))
            {
                if (activeGroup is not null)
                {
                    activeGroup.AddContinuation(logEvent);
                    continue;
                }

                groups.Add(new LogExceptionGroup(logEvent));
                activeGroup = null;
                continue;
            }

            if (IsExceptionRoot(logEvent))
            {
                activeGroup = new LogExceptionGroup(logEvent);
                groups.Add(activeGroup);
                continue;
            }

            activeGroup = null;
            groups.Add(new LogExceptionGroup(logEvent));
        }

        return groups;
    }

    public static IReadOnlyList<LogLineDisplayItem> Project(
        IReadOnlyList<LogEvent> events,
        LogExceptionFoldState foldState)
    {
        var items = new List<LogLineDisplayItem>();
        foreach (var group in BuildGroups(events))
        {
            if (!group.HasFoldableContent)
            {
                items.Add(LogLineDisplayItem.FromEvent(group.RootEvent));
                continue;
            }

            var expanded = foldState.IsExpanded(group.RootEvent.EventId);
            items.Add(LogLineDisplayItem.FromExceptionGroup(group, expanded, includeContinuations: expanded));

            if (!expanded)
                continue;

            if (group.IsStructured && group.RootEvent.Exception is not null)
            {
                foreach (var line in group.RootEvent.Exception.FormatExpandedLines().Skip(1))
                    items.Add(LogLineDisplayItem.FromStructuredExceptionLine(group, line));
                continue;
            }

            foreach (var continuation in group.ContinuationEvents)
                items.Add(LogLineDisplayItem.FromExceptionContinuation(group, continuation));
        }

        return items;
    }

    public static bool IsExceptionRoot(LogEvent logEvent)
    {
        if (logEvent.Exception is not null)
            return true;

        if (logEvent.Severity is not (LogSeverity.Error or LogSeverity.Fatal))
            return false;

        return !IsLegacyStackContinuation(logEvent);
    }

    public static bool IsLegacyStackContinuation(LogEvent logEvent)
    {
        if (logEvent.Exception is not null)
            return false;

        if (logEvent.Severity is LogSeverity.Error or LogSeverity.Fatal
            && LooksLikeExceptionHeader(logEvent.Message))
            return false;

        return IsStackLikeMessage(logEvent.Message);
    }

    private static bool LooksLikeExceptionHeader(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var trimmed = message.TrimStart();
        if (trimmed.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            return true;

        var colonIndex = trimmed.IndexOf(':');
        if (colonIndex <= 0)
            return false;

        var typeCandidate = trimmed[..colonIndex];
        return typeCandidate.Contains('.')
               && !typeCandidate.StartsWith("at ", StringComparison.Ordinal)
               && !typeCandidate.StartsWith("--->", StringComparison.Ordinal);
    }

    private static bool IsStackLikeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var trimmed = message.TrimStart();
        if (trimmed.StartsWith("at ", StringComparison.Ordinal)
            || trimmed.StartsWith("at ", StringComparison.OrdinalIgnoreCase))
            return true;

        if (trimmed.StartsWith("--->", StringComparison.Ordinal)
            || trimmed.StartsWith("InnerException", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("StackTrace:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("--- End of", StringComparison.OrdinalIgnoreCase))
            return true;

        return message.Length > 0
               && char.IsWhiteSpace(message[0])
               && trimmed.Contains(" at ", StringComparison.Ordinal);
    }
}
