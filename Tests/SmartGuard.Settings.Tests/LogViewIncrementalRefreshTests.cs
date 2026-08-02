using FluentAssertions;
using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings.Tests;

public class LogViewIncrementalRefreshTests
{
    [Fact]
    public void Tail_growth_uses_append_delta_until_display_window_slides()
    {
        var window = new LogEventWindow();
        window.Initialize(Enumerable.Range(0, 100).Select(i => TestEvent($"e:{i}")));

        var delta = window.Append([TestEvent("e:100")]);

        delta.AppendedEvents.Should().ContainSingle(e => e.EventId == "e:100");
        delta.RemovedHeadCount.Should().Be(0);
        window.Count.Should().Be(101);
    }

    [Fact]
    public void Sliding_window_after_slice_boundary_emits_trim_delta_instead_of_full_replace()
    {
        var window = new LogEventWindow();
        window.SetAnchorEventId("e:1500");
        window.Initialize(Enumerable.Range(0, LogEventWindow.MaxEventCount).Select(i => TestEvent($"e:{i}")));

        var delta = window.Append([TestEvent("e:new")]);

        delta.AppendedEvents.Should().ContainSingle(e => e.EventId == "e:new");
        delta.RemovedHeadCount.Should().Be(1);
        window.Count.Should().Be(LogEventWindow.MaxEventCount);
        window.Events.Should().NotContain(e => e.EventId == "e:0");
    }

    private static LogEvent TestEvent(string eventId)
        => new(
            LogEvent.CurrentSchemaVersion,
            eventId,
            DateTimeOffset.UtcNow,
            LogSeverity.Info,
            LogComponentRegistry.Legacy,
            LogChannelRegistry.Compatibility,
            EventName: null,
            "line",
            Exception: null,
            OperationId: null,
            TraceId: null,
            SpanId: null,
            UserActionId: null,
            ProcessId: null,
            ThreadId: null,
            LogEvent.EmptyProperties);
}
