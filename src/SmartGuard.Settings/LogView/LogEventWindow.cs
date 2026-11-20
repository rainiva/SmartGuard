using System.Text;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.LogView;

public sealed class LogEventWindow
{
    public const int MaxEventCount = 2000;
    public const int MaxPayloadBytes = 4 * 1024 * 1024;

    private readonly List<LogEvent> _events = [];
    private readonly HashSet<string> _eventIds = new(StringComparer.Ordinal);
    private string? _anchorEventId;

    public IReadOnlyList<LogEvent> Events => _events;

    public int Count => _events.Count;

    public int EstimatedPayloadBytes { get; private set; }

    public void SetAnchorEventId(string? anchorEventId)
        => _anchorEventId = anchorEventId;

    public LogWindowDelta Initialize(IEnumerable<LogEvent> events)
    {
        _events.Clear();
        _eventIds.Clear();
        EstimatedPayloadBytes = 0;

        foreach (var logEvent in events)
            TryAddCore(logEvent);

        var trim = EnforceBounds(TrimBias.PreferHead);
        return trim.IsEmpty
            ? LogWindowDelta.Initialize(_events.ToArray())
            : MergeInitialize(_events.ToArray(), trim);
    }

    public LogWindowDelta Append(IEnumerable<LogEvent> events)
    {
        var appended = new List<LogEvent>();
        foreach (var logEvent in events)
        {
            if (!TryAddCore(logEvent))
                continue;

            appended.Add(logEvent);
        }

        if (appended.Count == 0)
            return LogWindowDelta.None;

        var trim = EnforceBounds(TrimBias.PreferHead);
        return LogWindowDelta.AppendTail(appended, trim.RemovedHeadCount, trim.RemovedTailCount);
    }

    public LogWindowDelta Prepend(IEnumerable<LogEvent> events)
    {
        var incoming = events as IList<LogEvent> ?? events.ToList();
        if (incoming.Count == 0)
            return LogWindowDelta.None;

        var prepended = new List<LogEvent>();
        for (var i = incoming.Count - 1; i >= 0; i--)
        {
            var logEvent = incoming[i];
            if (_eventIds.Contains(logEvent.EventId))
                continue;

            _eventIds.Add(logEvent.EventId);
            _events.Insert(0, logEvent);
            EstimatedPayloadBytes += EstimatePayloadBytes(logEvent);
            prepended.Insert(0, logEvent);
        }

        if (prepended.Count == 0)
            return LogWindowDelta.None;

        var trim = EnforceBounds(TrimBias.PreferTail);
        return LogWindowDelta.PrependHead(prepended, trim.RemovedHeadCount, trim.RemovedTailCount);
    }

    private enum TrimBias
    {
        PreferHead,
        PreferTail,
    }

    private static LogWindowDelta MergeInitialize(IReadOnlyList<LogEvent> events, LogWindowDelta trim)
        => new()
        {
            IsInitialize = true,
            InitializeEvents = events,
            RemovedHeadCount = trim.RemovedHeadCount,
            RemovedTailCount = trim.RemovedTailCount,
        };

    internal static int EstimatePayloadBytes(LogEvent logEvent)
    {
        var bytes = Encoding.UTF8.GetByteCount(logEvent.EventId)
                    + Encoding.UTF8.GetByteCount(logEvent.Message)
                    + Encoding.UTF8.GetByteCount(logEvent.Component)
                    + Encoding.UTF8.GetByteCount(logEvent.Channel);

        if (!string.IsNullOrEmpty(logEvent.EventName))
            bytes += Encoding.UTF8.GetByteCount(logEvent.EventName);
        if (!string.IsNullOrEmpty(logEvent.OperationId))
            bytes += Encoding.UTF8.GetByteCount(logEvent.OperationId);
        if (!string.IsNullOrEmpty(logEvent.TraceId))
            bytes += Encoding.UTF8.GetByteCount(logEvent.TraceId);
        if (!string.IsNullOrEmpty(logEvent.SpanId))
            bytes += Encoding.UTF8.GetByteCount(logEvent.SpanId);
        if (!string.IsNullOrEmpty(logEvent.UserActionId))
            bytes += Encoding.UTF8.GetByteCount(logEvent.UserActionId);

        return bytes;
    }

    private bool TryAddCore(LogEvent logEvent)
    {
        if (!_eventIds.Add(logEvent.EventId))
            return false;

        _events.Add(logEvent);
        EstimatedPayloadBytes += EstimatePayloadBytes(logEvent);
        return true;
    }

    private LogWindowDelta EnforceBounds(TrimBias bias)
    {
        var removedHead = 0;
        var removedTail = 0;

        while (_events.Count > MaxEventCount || EstimatedPayloadBytes > MaxPayloadBytes)
        {
            if (_events.Count == 0)
                break;

            if (ShouldTrimFromTail(bias))
            {
                RemoveTailCore();
                removedTail++;
            }
            else
            {
                RemoveHeadCore();
                removedHead++;
            }
        }

        if (removedHead == 0 && removedTail == 0)
            return LogWindowDelta.None;

        return new LogWindowDelta
        {
            RemovedHeadCount = removedHead,
            RemovedTailCount = removedTail,
        };
    }

    private bool ShouldTrimFromTail(TrimBias bias)
    {
        if (string.IsNullOrEmpty(_anchorEventId))
            return bias == TrimBias.PreferTail;

        var anchorIndex = _events.FindIndex(e => e.EventId == _anchorEventId);
        if (anchorIndex < 0)
            return bias == TrimBias.PreferTail;

        var distanceToHead = anchorIndex;
        var distanceToTail = _events.Count - 1 - anchorIndex;
        return distanceToTail > distanceToHead
               || (distanceToTail == distanceToHead && bias == TrimBias.PreferTail);
    }

    private void RemoveHeadCore()
    {
        if (_events.Count == 0)
            return;

        var removed = _events[0];
        _events.RemoveAt(0);
        _eventIds.Remove(removed.EventId);
        EstimatedPayloadBytes -= EstimatePayloadBytes(removed);
    }

    private void RemoveTailCore()
    {
        if (_events.Count == 0)
            return;

        var removed = _events[^1];
        _events.RemoveAt(_events.Count - 1);
        _eventIds.Remove(removed.EventId);
        EstimatedPayloadBytes -= EstimatePayloadBytes(removed);
    }
}
