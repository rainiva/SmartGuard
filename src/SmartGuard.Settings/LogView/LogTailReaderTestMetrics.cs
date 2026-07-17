namespace SmartGuard.Settings.LogView;

internal static class LogTailReaderTestMetrics
{
    internal static int ReadFromOffsetCallCount { get; private set; }

    /// <summary>Test-only: artificial delay before disk reads to assert UI does not block.</summary>
    internal static int ArtificialDelayMs { get; set; }

    internal static void Reset()
    {
        ReadFromOffsetCallCount = 0;
        ArtificialDelayMs = 0;
    }

    internal static void RecordReadFromOffset() => ReadFromOffsetCallCount++;

    internal static void MaybeDelayForTests()
    {
        var ms = ArtificialDelayMs;
        if (ms > 0)
            Thread.Sleep(ms);
    }
}
