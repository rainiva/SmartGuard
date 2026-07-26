namespace SmartGuard.Settings.LogView;

internal static class LogTailReaderTestMetrics
{
    internal static int ReadFromOffsetCallCount { get; private set; }

    internal static Action? BeforeReadForTests { get; set; }

    /// <summary>Test-only: artificial delay before disk reads to assert UI does not block.</summary>
    internal static int ArtificialDelayMs { get; set; }

    internal static void Reset()
    {
        ReadFromOffsetCallCount = 0;
        BeforeReadForTests = null;
        ArtificialDelayMs = 0;
    }

    internal static void RecordReadFromOffset() => ReadFromOffsetCallCount++;

    internal static void MaybeDelayForTests()
    {
        BeforeReadForTests?.Invoke();

        var ms = ArtificialDelayMs;
        if (ms > 0)
            Thread.Sleep(ms);
    }
}
