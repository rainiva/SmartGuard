using FluentAssertions;
using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings.Tests;

public class LogEventWindowTests
{
    private static LogEvent Event(string eventId, string message = "message")
        => new(
            LogEvent.CurrentSchemaVersion,
            eventId,
            DateTimeOffset.UtcNow,
            LogSeverity.Info,
            LogComponentRegistry.Legacy,
            LogChannelRegistry.Compatibility,
            EventName: null,
            message,
            Exception: null,
            OperationId: null,
            TraceId: null,
            SpanId: null,
            UserActionId: null,
            ProcessId: null,
            ThreadId: null,
            LogEvent.EmptyProperties);

    [Fact]
    public void Append_returns_append_tail_delta_with_appended_events()
    {
        var window = new LogEventWindow();
        window.Initialize([Event("e:1", "first")]);

        var delta = window.Append([Event("e:2", "second"), Event("e:3", "third")]);

        delta.AppendedEvents.Should().HaveCount(2);
        delta.PrependedEvents.Should().BeEmpty();
        delta.RemovedHeadCount.Should().Be(0);
        delta.RemovedTailCount.Should().Be(0);
        window.Events.Select(e => e.EventId).Should().Equal("e:1", "e:2", "e:3");
    }

    [Fact]
    public void Prepend_returns_prepend_head_delta_with_prepended_events()
    {
        var window = new LogEventWindow();
        window.Initialize([Event("e:2", "second")]);

        var delta = window.Prepend([Event("e:1", "first")]);

        delta.PrependedEvents.Should().ContainSingle(e => e.EventId == "e:1");
        delta.AppendedEvents.Should().BeEmpty();
        window.Events.Select(e => e.EventId).Should().Equal("e:1", "e:2");
    }

    [Fact]
    public void Duplicate_event_ids_are_ignored()
    {
        var window = new LogEventWindow();
        window.Initialize([Event("e:1")]);

        var appendDelta = window.Append([Event("e:1"), Event("e:2")]);
        appendDelta.AppendedEvents.Should().ContainSingle(e => e.EventId == "e:2");

        var prependDelta = window.Prepend([Event("e:1"), Event("e:0")]);
        prependDelta.PrependedEvents.Should().ContainSingle(e => e.EventId == "e:0");
        window.Count.Should().Be(3);
    }

    [Fact]
    public void Append_over_event_count_limit_trims_away_from_anchor()
    {
        var window = new LogEventWindow();
        window.SetAnchorEventId("e:1999");
        var initial = Enumerable.Range(0, LogEventWindow.MaxEventCount)
            .Select(i => Event($"e:{i}"))
            .ToList();
        window.Initialize(initial);

        var delta = window.Append([Event("e:new")]);

        window.Count.Should().Be(LogEventWindow.MaxEventCount);
        delta.RemovedHeadCount.Should().Be(1);
        delta.RemovedTailCount.Should().Be(0);
        window.Events.Should().Contain(e => e.EventId == "e:1999");
        window.Events.Should().Contain(e => e.EventId == "e:new");
        window.Events.Should().NotContain(e => e.EventId == "e:0");
    }

    [Fact]
    public void Prepend_over_event_count_limit_trims_tail_when_anchor_is_near_head()
    {
        var window = new LogEventWindow();
        window.SetAnchorEventId("e:0");
        var initial = Enumerable.Range(0, LogEventWindow.MaxEventCount)
            .Select(i => Event($"e:{i}"))
            .ToList();
        window.Initialize(initial);

        var delta = window.Prepend([Event("e:older")]);

        window.Count.Should().Be(LogEventWindow.MaxEventCount);
        delta.RemovedTailCount.Should().Be(1);
        delta.RemovedHeadCount.Should().Be(0);
        window.Events[0].EventId.Should().Be("e:older");
        window.Events.Should().Contain(e => e.EventId == "e:0");
        window.Events.Should().NotContain(e => e.EventId == $"e:{LogEventWindow.MaxEventCount - 1}");
    }

    [Fact]
    public void Append_over_payload_limit_trims_from_head_when_anchor_is_near_tail()
    {
        var window = new LogEventWindow();
        window.SetAnchorEventId("e:tail");
        var messageSize = (LogEventWindow.MaxPayloadBytes / 3) + 512;
        var largeMessage = new string('x', messageSize);
        window.Initialize([
            Event("e:head", largeMessage),
            Event("e:tail", largeMessage),
        ]);

        var delta = window.Append([Event("e:new", largeMessage)]);

        window.EstimatedPayloadBytes.Should().BeLessThanOrEqualTo(LogEventWindow.MaxPayloadBytes);
        delta.RemovedHeadCount.Should().BeGreaterThan(0);
        window.Events.Should().Contain(e => e.EventId == "e:tail");
        window.Events.Should().Contain(e => e.EventId == "e:new");
    }

    [Fact]
    public void Remove_head_and_tail_counts_are_reflected_in_combined_delta()
    {
        var window = new LogEventWindow();
        window.SetAnchorEventId("e:500");
        window.Initialize(Enumerable.Range(0, LogEventWindow.MaxEventCount).Select(i => Event($"e:{i}")));

        var delta = window.Prepend(Enumerable.Range(-200, 200).Select(i => Event($"e:{i}")));

        delta.PrependedEvents.Should().HaveCount(200);
        delta.RemovedTailCount.Should().Be(200);
        delta.RemovedHeadCount.Should().Be(0);
        window.Count.Should().Be(LogEventWindow.MaxEventCount);
        window.Events.Should().Contain(e => e.EventId == "e:500");
        window.Events.Should().Contain(e => e.EventId == "e:-1");
    }
}
