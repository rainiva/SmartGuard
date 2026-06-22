namespace SmartGuard.Settings;

public sealed record LogViewDisplaySliceResult(
    IReadOnlyList<string> Lines,
    int TotalMatchedCount,
    bool IsTruncated);

public static class LogViewDisplaySlice
{
    public const int DefaultMaxLines = 500;

    public static LogViewDisplaySliceResult Select(
        IReadOnlyList<string> lines,
        int maxLines,
        bool preferTail)
    {
        if (lines.Count <= maxLines)
            return new LogViewDisplaySliceResult(lines, lines.Count, IsTruncated: false);

        var start = preferTail ? lines.Count - maxLines : 0;
        var slice = lines.Skip(start).Take(maxLines).ToList();
        return new LogViewDisplaySliceResult(slice, lines.Count, IsTruncated: true);
    }
}
