namespace SmartGuard.LogViewer;

public static class LogViewerTextTrimmer
{
    public const int DefaultMaxCachedBytes = 262_144;

    public static string TrimToMaxBytes(string text, int maxBytes = DefaultMaxCachedBytes)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxBytes)
            return text;

        var start = text.Length - maxBytes;
        var slice = text[start..];
        var newline = slice.IndexOf('\n');
        if (newline >= 0 && newline + 1 < slice.Length)
            return slice[(newline + 1)..];

        return slice;
    }
}
