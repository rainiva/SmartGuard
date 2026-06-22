namespace SmartGuard.Settings;

public enum LogViewUpdateMode
{
    NoChange,
    AppendTail,
    ReplaceAll,
}

public sealed record LogViewUpdatePlan(
    LogViewUpdateMode Mode,
    IReadOnlyList<string> AllLines,
    IReadOnlyList<string> AppendedLines)
{
    public static LogViewUpdatePlan NoChange(IReadOnlyList<string> lines)
        => new(LogViewUpdateMode.NoChange, lines, []);

    public static LogViewUpdatePlan AppendTail(IReadOnlyList<string> appendedLines, IReadOnlyList<string> allLines)
        => new(LogViewUpdateMode.AppendTail, allLines, appendedLines);

    public static LogViewUpdatePlan ReplaceAll(IReadOnlyList<string> allLines)
        => new(LogViewUpdateMode.ReplaceAll, allLines, []);
}

public static class LogViewUpdatePlanner
{
    public static LogViewUpdatePlan CreatePlan(
        IReadOnlyList<string> previousDisplayLines,
        IReadOnlyList<string> nextDisplayLines,
        bool forceReplace)
    {
        if (forceReplace)
            return LogViewUpdatePlan.ReplaceAll(nextDisplayLines);

        if (previousDisplayLines.Count == 0)
            return LogViewUpdatePlan.ReplaceAll(nextDisplayLines);

        if (previousDisplayLines.SequenceEqual(nextDisplayLines))
            return LogViewUpdatePlan.NoChange(nextDisplayLines);

        if (nextDisplayLines.Count > previousDisplayLines.Count
            && HasPrefix(previousDisplayLines, nextDisplayLines))
        {
            var appended = nextDisplayLines
                .Skip(previousDisplayLines.Count)
                .ToList();
            return LogViewUpdatePlan.AppendTail(appended, nextDisplayLines);
        }

        return LogViewUpdatePlan.ReplaceAll(nextDisplayLines);
    }

    private static bool HasPrefix(IReadOnlyList<string> prefix, IReadOnlyList<string> lines)
    {
        for (var i = 0; i < prefix.Count; i++)
        {
            if (!string.Equals(prefix[i], lines[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
