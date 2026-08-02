using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using SmartGuard.Configuration;
using SmartGuard.Settings;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class LogViewportControllerTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void Prepend_restore_anchor_within_one_dip_at_various_dpi(double dpiScale)
    {
        WpfStaTestHost.Run(() =>
        {
            using var ctx = CreateListContext(dpiScale, lineCount: 80, out var window, out var presenter, out var viewport);
            ScrollToMiddle(window, viewport);

            var anchor = viewport.CaptureAnchor();
            anchor.Should().NotBeNull();

            var beforeOffset = viewport.ScrollViewer!.VerticalOffset;
            var delta = CreatePrependDelta(presenter, prependCount: 5);
            presenter.ApplyDelta(delta);
            window.UpdateLayout();

            viewport.RestoreAnchor(anchor!);
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            viewport.ScrollViewer!.VerticalOffset.Should().BeApproximately(
                beforeOffset,
                1.0,
                "prepend must restore EventId + in-item pixel offset within 1 DIP");
        });
    }

    [Fact]
    public void Append_while_reading_history_preserves_anchor()
    {
        WpfStaTestHost.Run(() =>
        {
            using var ctx = CreateListContext(1.0, lineCount: 60, out var window, out var presenter, out var viewport);
            viewport.SetFollowLatest(false);
            SyncFollowCheckBoxForTests(viewport);
            ScrollToMiddle(window, viewport);

            var anchor = viewport.CaptureAnchor();
            anchor.Should().NotBeNull();
            var beforeOffset = viewport.ScrollViewer!.VerticalOffset;

            var delta = CreateAppendDelta(presenter, appendCount: 3);
            presenter.ApplyDelta(delta);
            window.UpdateLayout();

            viewport.OnDataApplied(LogViewApplyResult.Appended, anchor);
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            viewport.ScrollViewer!.VerticalOffset.Should().BeApproximately(
                beforeOffset,
                1.0,
                "append while reading history must keep the captured anchor");
        });
    }

    [Fact]
    public void Stale_dispatcher_generation_does_not_apply_scroll()
    {
        WpfStaTestHost.Run(() =>
        {
            using var ctx = CreateListContext(1.0, lineCount: 40, out var window, out _, out var viewport);
            viewport.SetFollowLatest(true);
            viewport.ScrollToTopImmediate();

            viewport.BeginDeferredTailScrollForTests();
            var staleGeneration = viewport.ScrollGenerationForTests;
            viewport.ScrollToTopImmediate();
            viewport.CompleteDeferredTailScrollForTests(staleGeneration);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            viewport.ScrollViewer!.VerticalOffset.Should().BeApproximately(
                0,
                1.0,
                "stale deferred scroll commands must be discarded when generation advances");
        });
    }

    private static LogWindowDelta CreatePrependDelta(LogViewListPresenter presenter, int prependCount)
    {
        var events = Enumerable.Range(0, prependCount)
            .Select(i => CreateEvent($"e:prepend-{i}", $"prepended {i}"))
            .ToList();
        return LogWindowDelta.PrependHead(events);
    }

    private static LogWindowDelta CreateAppendDelta(LogViewListPresenter presenter, int appendCount)
    {
        var start = presenter.Items.Count;
        var events = Enumerable.Range(0, appendCount)
            .Select(i => CreateEvent($"e:append-{start + i}", $"appended {start + i}"))
            .ToList();
        return LogWindowDelta.AppendTail(events);
    }

    private static LogEvent CreateEvent(string eventId, string message)
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

    private static void SyncFollowCheckBoxForTests(LogViewportController viewport)
    {
        var method = typeof(LogViewportController).GetMethod(
            "SyncFollowCheckBox",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method!.Invoke(viewport, null);
    }

    private static void ScrollToMiddle(Window window, LogViewportController viewport)
    {
        var scrollViewer = viewport.ScrollViewer!;
        scrollViewer.UpdateLayout();
        var target = scrollViewer.ScrollableHeight / 2;
        viewport.ScrollToVerticalOffsetImmediate(target);
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        scrollViewer.VerticalOffset.Should().BeGreaterThan(10);
    }

    private static Scope CreateListContext(
        double dpiScale,
        int lineCount,
        out Window window,
        out LogViewListPresenter presenter,
        out LogViewportController viewport)
    {
        window = new Window
        {
            Width = 420,
            Height = 360,
            ShowInTaskbar = false,
        };

        if (dpiScale != 1.0)
        {
            window.LayoutTransform = new System.Windows.Media.ScaleTransform(dpiScale, dpiScale);
        }

        var listBox = new ListBox();
        LogViewListPresenter.ConfigureListBox(listBox);
        presenter = new LogViewListPresenter();
        presenter.Attach(listBox);
        window.Content = listBox;

        WpfStaTestHost.ShowAndWait(window);
        listBox.UpdateLayout();

        var initial = Enumerable.Range(0, lineCount)
            .Select(i => CreateEvent($"e:{i:D4}", $"[INFO] line {i}"))
            .ToList();
        presenter.ApplyDelta(LogWindowDelta.Initialize(initial));
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        var listBoxRef = listBox;
        var presenterRef = presenter;
        viewport = new LogViewportController(window, () => listBoxRef, () => presenterRef, () => null, _ => { });
        viewport.EnsureHooked(listBox, presenter);
        viewport.ScrollViewer.Should().NotBeNull();

        return new Scope(window);
    }

    private sealed class Scope : IDisposable
    {
        private readonly Window _window;

        public Scope(Window window) => _window = window;

        public void Dispose() => _window.Close();
    }
}
