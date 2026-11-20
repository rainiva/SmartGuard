using System.Windows;
using System.Windows.Controls;
using System.Text;
using FluentAssertions;
using SmartGuard.Configuration;
using SmartGuard.Settings;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class LogExceptionGroupProjectorTests
{
    private static LogEvent StructuredError(
        string eventId,
        string type,
        string message,
        LogExceptionInfo? inner = null,
        string? stackTrace = " at Outer()")
        => new(
            LogEvent.CurrentSchemaVersion,
            eventId,
            DateTimeOffset.UtcNow,
            LogSeverity.Error,
            LogComponentRegistry.Engine,
            LogChannelRegistry.Diagnostics,
            EventNameRegistry.IterationError,
            message,
            new LogExceptionInfo(type, message, stackTrace, inner),
            OperationId: null,
            TraceId: null,
            SpanId: null,
            UserActionId: null,
            ProcessId: null,
            ThreadId: null,
            LogEvent.EmptyProperties);

    private static LogEvent LegacyError(string eventId, string message)
        => LegacyEvent(eventId, LogSeverity.Error, message);

    private static LogEvent LegacyInfo(string eventId, string message)
        => LegacyEvent(eventId, LogSeverity.Info, message);

    private static LogEvent LegacyEvent(string eventId, LogSeverity severity, string message)
        => new(
            LogEvent.CurrentSchemaVersion,
            eventId,
            DateTimeOffset.UtcNow,
            severity,
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
    public void Structured_exception_projects_as_single_collapsed_group()
    {
        var inner = new LogExceptionInfo("System.IO.IOException", "disk full", " at Inner()", Inner: null);
        var root = StructuredError("e:1", "System.InvalidOperationException", "write failed", inner);

        var groups = LogExceptionGroupProjector.BuildGroups([root]);

        groups.Should().ContainSingle();
        groups[0].RootEvent.EventId.Should().Be("e:1");
        groups[0].IsStructured.Should().BeTrue();
        groups[0].InnerCount.Should().Be(1);
        groups[0].ContinuationEvents.Should().BeEmpty();
    }

    [Fact]
    public void Legacy_stack_continuation_attaches_to_nearest_error_root()
    {
        var root = LegacyError("e:root", "System.InvalidOperationException: boom");
        var frame = LegacyEvent("e:frame", LogSeverity.Info, "   at SmartGuard.Engine.Worker.Run()");
        var unrelated = LegacyInfo("e:info", "heartbeat ok");

        var groups = LogExceptionGroupProjector.BuildGroups([root, frame, unrelated]);

        groups.Should().HaveCount(2);
        groups[0].RootEvent.EventId.Should().Be("e:root");
        groups[0].ContinuationEvents.Select(e => e.EventId).Should().Equal("e:frame");
        groups[1].RootEvent.EventId.Should().Be("e:info");
    }

    [Fact]
    public void Adjacent_different_exceptions_do_not_cross_group()
    {
        var firstRoot = LegacyError("e:a", "System.TimeoutException: first");
        var firstFrame = LegacyEvent("e:a1", LogSeverity.Info, "   at First()");
        var secondRoot = LegacyError("e:b", "System.ArgumentException: second");
        var secondFrame = LegacyEvent("e:b1", LogSeverity.Info, "   at Second()");

        var groups = LogExceptionGroupProjector.BuildGroups(
            [firstRoot, firstFrame, secondRoot, secondFrame]);

        groups.Should().HaveCount(2);
        groups[0].ContinuationEvents.Should().ContainSingle(e => e.EventId == "e:a1");
        groups[1].ContinuationEvents.Should().ContainSingle(e => e.EventId == "e:b1");
    }

    [Fact]
    public void Project_collapsed_structured_exception_hides_stack_in_body()
    {
        var inner = new LogExceptionInfo("System.IO.IOException", "disk full", " at Inner()", Inner: null);
        var root = StructuredError("e:1", "System.InvalidOperationException", "write failed", inner);
        var foldState = new LogExceptionFoldState();

        var items = LogExceptionGroupProjector.Project([root], foldState);

        items.Should().ContainSingle();
        items[0].BodyText.Should().Contain("System.InvalidOperationException");
        items[0].BodyText.Should().Contain("write failed");
        items[0].BodyText.Should().Contain("+1 inner");
        items[0].IsExceptionRoot.Should().BeTrue();
        items[0].IsExceptionFolded.Should().BeTrue();
    }

    [Fact]
    public void Project_expanded_structured_exception_shows_stack_lines()
    {
        var root = StructuredError("e:1", "System.InvalidOperationException", "write failed");
        var foldState = new LogExceptionFoldState();
        foldState.SetExpanded("e:1", expanded: true);

        var items = LogExceptionGroupProjector.Project([root], foldState);

        items.Should().HaveCountGreaterThan(1);
        items[0].IsExceptionRoot.Should().BeTrue();
        items[0].IsExceptionFolded.Should().BeFalse();
        items.Skip(1).Should().OnlyContain(item => item.IsExceptionContinuation);
        items.Select(item => item.ExportLineText).Should().Contain(line => line.Contains(" at Outer()"));
    }

    [Fact]
    public void Export_line_text_preserves_full_exception_for_copy()
    {
        var inner = new LogExceptionInfo("System.IO.IOException", "disk full", " at Inner()", Inner: null);
        var root = StructuredError("e:1", "System.InvalidOperationException", "write failed", inner);
        var foldState = new LogExceptionFoldState();

        var items = LogExceptionGroupProjector.Project([root], foldState);

        items.Should().ContainSingle();
        items[0].ExportLineText.Should().Contain("System.InvalidOperationException");
        items[0].ExportLineText.Should().Contain(" at Outer()");
        items[0].ExportLineText.Should().Contain("System.IO.IOException");
        items[0].ExportLineText.Should().Contain(" at Inner()");
    }

    [Fact]
    public void Toggle_fold_updates_items_without_collection_reset()
    {
        RunOnSta(() =>
        {
            var presenter = new LogViewListPresenter();
            presenter.Attach(CreateListBox());
            var resetCount = TrackResetCount(presenter.Items);

            var root = StructuredError("e:1", "System.InvalidOperationException", "write failed");
            presenter.ApplyDelta(LogWindowDelta.Initialize([root]));

            presenter.Items.Should().ContainSingle();
            presenter.ToggleExceptionFold("e:1");
            presenter.Items.Count.Should().BeGreaterThan(1);
            resetCount.Value.Should().Be(0);

            presenter.ToggleExceptionFold("e:1");
            presenter.Items.Should().ContainSingle();
            resetCount.Value.Should().Be(0);
        });
    }

    [Fact]
    public void Toggle_fold_preserves_anchor_event_id()
    {
        RunOnSta(() =>
        {
            var presenter = new LogViewListPresenter();
            var listBox = CreateListBox();
            presenter.Attach(listBox);
            WpfStaTestHost.ShowAndWait(new Window { Content = listBox, Width = 400, Height = 120 });
            presenter.EnsureScrollViewerResolved();

            var events = Enumerable.Range(0, 20)
                .Select(i => LegacyInfo($"e:{i}", $"line {i}"))
                .Append(StructuredError("e:ex", "System.Exception", "failure"))
                .ToList();
            presenter.ApplyDelta(LogWindowDelta.Initialize(events));

            const string anchorEventId = "e:10";
            presenter.ScrollViewer!.ScrollToVerticalOffset(240);
            presenter.ScrollViewer.UpdateLayout();
            var anchorBefore = presenter.GetEventIdAt(presenter.FindIndexByEventId(anchorEventId));

            presenter.ToggleExceptionFold("e:ex");

            presenter.GetEventIdAt(presenter.FindIndexByEventId(anchorEventId)).Should().Be(anchorBefore);
        });
    }

    [Fact]
    public void Exception_double_click_expands_only_the_selected_page_row()
    {
        var source = File.ReadAllText(
            Path.Combine(SettingsSourcePaths.RepoRoot(), "src/SmartGuard.Settings/SettingsLogPageHostWiring.cs"),
            Encoding.UTF8);

        source.Should().Contain("WireLogTableExceptionFold");
        source.Should().Contain("DataGridRow");
        source.Should().Contain("DetailsVisibility");
    }

    private static ResetCounter TrackResetCount(System.Collections.Specialized.INotifyCollectionChanged items)
    {
        var counter = new ResetCounter();
        items.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                counter.Value++;
        };

        return counter;
    }

    private sealed class ResetCounter
    {
        public int Value { get; set; }
    }

    private static ListBox CreateListBox()
    {
        var listBox = new ListBox();
        LogViewListPresenter.ConfigureListBox(listBox);
        return listBox;
    }

    private static void RunOnSta(Action action) => WpfStaTestHost.Run(action);
}
