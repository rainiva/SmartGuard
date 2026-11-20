using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using SmartGuard.Configuration;
using SmartGuard.Settings;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class LogViewListPresenterTests
{
    [Fact]
    public void Apply_replace_all_populates_virtualized_list_items()
    {
        RunOnSta(() =>
        {
            var listBox = CreateListBox();
            var presenter = new LogViewListPresenter();
            presenter.Attach(listBox);

            presenter.Apply(LogViewUpdatePlan.ReplaceAll(
                ["[INFO] 2026-06-21 10:00:00 alpha", "[WARN] 2026-06-21 10:01:00 beta"]));

            presenter.GetPlainText().Should().Contain("alpha");
            presenter.GetPlainText().Should().Contain("beta");
            listBox.Items.Count.Should().Be(2);
        });
    }

    [Fact]
    public void Apply_append_tail_adds_items_without_rebuilding_existing_rows()
    {
        RunOnSta(() =>
        {
            var listBox = CreateListBox();
            var presenter = new LogViewListPresenter();
            presenter.Attach(listBox);

            presenter.Apply(LogViewUpdatePlan.ReplaceAll(["[INFO] line 1"]));
            var initialItem = listBox.Items[0];

            presenter.Apply(LogViewUpdatePlan.AppendTail(
                ["[INFO] line 2"],
                ["[INFO] line 1", "[INFO] line 2"]));

            listBox.Items.Count.Should().Be(2);
            ReferenceEquals(listBox.Items[0], initialItem).Should().BeTrue();
            presenter.GetPlainText().Should().Be("[INFO] line 1" + Environment.NewLine + "[INFO] line 2");
        });
    }

    [Fact]
    public void Apply_no_change_keeps_items_and_returns_unchanged()
    {
        RunOnSta(() =>
        {
            var presenter = new LogViewListPresenter();
            presenter.Attach(CreateListBox());

            presenter.Apply(LogViewUpdatePlan.ReplaceAll(["[INFO] line 1"]));
            var result = presenter.Apply(LogViewUpdatePlan.NoChange(["[INFO] line 1"]));

            result.Should().Be(LogViewApplyResult.Unchanged);
            presenter.GetPlainText().Should().Be("[INFO] line 1");
        });
    }

    [Fact]
    public void ApplyDelta_incremental_updates_do_not_emit_collection_reset()
    {
        RunOnSta(() =>
        {
            var presenter = new LogViewListPresenter();
            var listBox = CreateListBox();
            presenter.Attach(listBox);

            var resetCount = TrackResetCount(presenter.Items);
            var window = new LogEventWindow();
            presenter.ApplyDelta(window.Initialize([TestEvent("e:1")]));
            presenter.ApplyDelta(window.Append([TestEvent("e:2"), TestEvent("e:3")]));
            presenter.ApplyDelta(window.Prepend([TestEvent("e:0")]));

            resetCount.Value.Should().Be(0);
            presenter.GetEventIdAt(0).Should().Be("e:0");
            listBox.Items.Count.Should().Be(4);
        });
    }

    [Fact]
    public void ApplyDelta_prepend_keeps_anchor_event_id_without_scrolling()
    {
        RunOnSta(() =>
        {
            var presenter = new LogViewListPresenter();
            var listBox = CreateListBox();
            presenter.Attach(listBox);
            WpfStaTestHost.ShowAndWait(new Window { Content = listBox, Width = 400, Height = 120 });
            presenter.EnsureScrollViewerResolved();

            var window = new LogEventWindow();
            window.SetAnchorEventId("e:10");
            window.Initialize(Enumerable.Range(0, 50).Select(i => TestEvent($"e:{i}")));
            presenter.ApplyDelta(LogWindowDelta.Initialize(window.Events.ToArray()));

            var scrollViewer = presenter.ScrollViewer;
            scrollViewer.Should().NotBeNull();
            scrollViewer!.ScrollToVerticalOffset(480);
            scrollViewer.UpdateLayout();
            const string anchorEventId = "e:10";
            var prependEvents = Enumerable.Range(-200, 200).Select(i => TestEvent($"e:{i}")).ToList();
            presenter.ApplyDelta(window.Prepend(prependEvents));

            presenter.Items.Select(item => item.EventId).Should().Contain(anchorEventId);
        });
    }

    [Fact]
    public void ApplyDelta_initialize_is_the_only_full_rebuild_and_subsequent_apply_avoids_reset()
    {
        RunOnSta(() =>
        {
            var presenter = new LogViewListPresenter();
            presenter.Attach(CreateListBox());
            var resetCount = TrackResetCount(presenter.Items);
            var window = new LogEventWindow();

            presenter.ApplyDelta(window.Initialize([TestEvent("e:1"), TestEvent("e:2")]));
            presenter.Apply(LogViewUpdatePlan.ReplaceAll(["[INFO] line 9", "[WARN] line 10"]));

            resetCount.Value.Should().Be(0);
            presenter.GetPlainText().Should().Contain("line 9");
        });
    }

    [Fact]
    public void ListBox_is_configured_for_virtualization()
    {
        RunOnSta(() =>
        {
            var listBox = CreateListBox();
            VirtualizingPanel.GetIsVirtualizing(listBox).Should().BeTrue();
            VirtualizingPanel.GetVirtualizationMode(listBox).Should().Be(VirtualizationMode.Recycling);
            VirtualizingPanel.GetScrollUnit(listBox).Should().Be(ScrollUnit.Pixel);
            ScrollViewer.GetCanContentScroll(listBox).Should().BeTrue();
        });
    }

    private static ResetCounter TrackResetCount(INotifyCollectionChanged items)
    {
        var counter = new ResetCounter();
        items.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset)
                counter.Value++;
        };

        return counter;
    }

    private static LogEvent TestEvent(string eventId, string message = "message")
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
