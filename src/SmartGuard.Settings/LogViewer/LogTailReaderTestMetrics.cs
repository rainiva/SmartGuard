namespace SmartGuard.LogViewer;

internal static class LogTailReaderTestMetrics
{
    internal static int ReadFromOffsetCallCount { get; private set; }

    internal static void Reset() => ReadFromOffsetCallCount = 0;

    internal static void RecordReadFromOffset() => ReadFromOffsetCallCount++;
}
