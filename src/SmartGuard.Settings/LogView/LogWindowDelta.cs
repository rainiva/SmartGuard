using SmartGuard.Configuration;

namespace SmartGuard.Settings.LogView;

public sealed record LogWindowDelta
{
    public bool IsInitialize { get; init; }

    public IReadOnlyList<LogEvent> InitializeEvents { get; init; } = [];

    public IReadOnlyList<LogEvent> PrependedEvents { get; init; } = [];

    public IReadOnlyList<LogEvent> AppendedEvents { get; init; } = [];

    public int RemovedHeadCount { get; init; }

    public int RemovedTailCount { get; init; }

    public static LogWindowDelta None { get; } = new();

    public bool IsEmpty =>
        !IsInitialize
        && PrependedEvents.Count == 0
        && AppendedEvents.Count == 0
        && RemovedHeadCount == 0
        && RemovedTailCount == 0;

    public static LogWindowDelta Initialize(IReadOnlyList<LogEvent> events)
        => new() { IsInitialize = true, InitializeEvents = events };

    public static LogWindowDelta AppendTail(IReadOnlyList<LogEvent> events, int removedHeadCount = 0, int removedTailCount = 0)
        => new()
        {
            AppendedEvents = events,
            RemovedHeadCount = removedHeadCount,
            RemovedTailCount = removedTailCount,
        };

    public static LogWindowDelta PrependHead(IReadOnlyList<LogEvent> events, int removedHeadCount = 0, int removedTailCount = 0)
        => new()
        {
            PrependedEvents = events,
            RemovedHeadCount = removedHeadCount,
            RemovedTailCount = removedTailCount,
        };

    public static LogWindowDelta RemoveHead(int count)
        => new() { RemovedHeadCount = count };

    public static LogWindowDelta RemoveTail(int count)
        => new() { RemovedTailCount = count };
}
