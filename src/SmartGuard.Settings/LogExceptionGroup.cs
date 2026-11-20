using SmartGuard.Configuration;

namespace SmartGuard.Settings;

public sealed class LogExceptionGroup
{
    private readonly List<LogEvent> _continuationEvents = [];

    public LogExceptionGroup(LogEvent rootEvent)
    {
        RootEvent = rootEvent;
    }

    public LogEvent RootEvent { get; }

    public IReadOnlyList<LogEvent> ContinuationEvents => _continuationEvents;

    public bool IsStructured => RootEvent.Exception is not null;

    public bool HasFoldableContent => IsStructured || _continuationEvents.Count > 0;

    public int InnerCount => RootEvent.Exception?.CountInners() ?? 0;

    internal void AddContinuation(LogEvent continuationEvent)
        => _continuationEvents.Add(continuationEvent);
}
