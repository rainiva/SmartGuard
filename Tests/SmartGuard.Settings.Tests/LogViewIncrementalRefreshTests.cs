using FluentAssertions;
using SmartGuard.Configuration;
using SmartGuard.Settings;
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

    [Fact]
    public async Task LoadLatest_reads_last_page_without_follow_tail()
    {
        var path = WriteTempLog(250);
        try
        {
            var controller = new LogViewController(path) { FollowTail = false };
            var result = await controller.RefreshAsync(LogViewRefreshKind.Latest, CancellationToken.None);

            result.Snapshot.FilteredLines.Should().HaveCount(LogTailReader.DefaultTargetEventCount);
            result.Snapshot.HasOlderHistory.Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadOlder_prepends_previous_page()
    {
        var path = WriteTempLog(450);
        try
        {
            var controller = new LogViewController(path);
            await controller.RefreshAsync(LogViewRefreshKind.Latest, CancellationToken.None);
            var before = controller.GetSnapshot().FilteredLines[^1];

            var result = await controller.RefreshAsync(LogViewRefreshKind.Older, CancellationToken.None);

            result.WindowDelta.PrependedEvents.Should().NotBeEmpty();
            result.Snapshot.FilteredLines.Count.Should().BeGreaterThan(LogTailReader.DefaultTargetEventCount);
            result.Snapshot.FilteredLines[^1].Should().Be(before);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadNewer_appends_growth_after_latest_page()
    {
        var path = WriteTempLog(200);
        try
        {
            var controller = new LogViewController(path) { FollowTail = true };
            await controller.RefreshAsync(LogViewRefreshKind.Latest, CancellationToken.None);
            File.AppendAllText(path, "[INFO] 2026-06-19 10:05:00 appended line\n");

            controller.RefreshFromDisk();
            var result = controller.CreateRefreshResult();

            result.WindowDelta.AppendedEvents.Should().NotBeEmpty();
            result.Snapshot.FilteredLines.Should().Contain(line => line.Contains("appended line"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempLog(int lineCount)
    {
        var path = Path.GetTempFileName();
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < lineCount; i++)
            builder.AppendLine($"[INFO] 2026-06-19 10:00:{i % 60:D2} [Engine] entry {i}");
        File.WriteAllText(path, builder.ToString());
        return path;
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
