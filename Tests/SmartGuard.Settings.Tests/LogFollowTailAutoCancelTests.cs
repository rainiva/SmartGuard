using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

// Regression for the follow-tail auto-cancel bug: scrolling up sometimes
// failed to uncheck 跟随最新, so the next refresh snapped the viewport back
// to the tail while the user was reading history.
[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class LogFollowTailAutoCancelTests
{
    [Fact]
    public void Wheel_scroll_up_during_refresh_suppression_window_cancels_follow_tail()
    {
        WpfStaTestHost.Run(() =>
        {
            var ctx = CreateShownLogPageContext(out var window, out var scrollViewer, out var controller);
            using (ctx)
            {
                var followTail = (CheckBox)window.FindName("chkLogFollowTail");
                followTail.IsChecked = true;
                var viewport = GetLogViewport(controller);

                viewport.ScrollToTopImmediate();
                viewport.PendingInitialTailScroll = true;
                viewport.ApplyFollowLatestIfEnabled();
                viewport.SuppressFollowAutoSync.Should().BeTrue(
                    "the test must exercise the suppression window that used to swallow the cancel");

                scrollViewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent,
                    Source = scrollViewer,
                });

                followTail.IsChecked.Should().BeFalse(
                    "wheeling up is an explicit user gesture and must cancel follow-tail even " +
                    "inside the programmatic-scroll suppression window");
            }
        });
    }

    [Fact]
    public void Wheel_scroll_up_from_tail_is_not_rechecked_before_the_viewport_moves()
    {
        WpfStaTestHost.Run(() =>
        {
            var ctx = CreateShownLogPageContext(out var window, out var scrollViewer, out var controller);
            using (ctx)
            {
                var followTail = (CheckBox)window.FindName("chkLogFollowTail");
                followTail.IsChecked = true;
                var viewport = GetLogViewport(controller);

                scrollViewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent,
                    Source = scrollViewer,
                });

                viewport.HandleScrollChangedForTests(scrollViewer);

                followTail.IsChecked.Should().BeFalse(
                    "the first layout event can still report the old tail offset and must not undo the user's upward gesture");
            }
        });
    }

    [Fact]
    public void Bottom_button_reaching_tail_checks_follow_tail()
    {
        WpfStaTestHost.Run(() =>
        {
            var ctx = CreateShownLogPageContext(out var window, out var scrollViewer, out _);
            using (ctx)
            {
                var followTail = (CheckBox)window.FindName("chkLogFollowTail");
                var topButton = (Button)window.FindName("btnLogScrollTop");
                var bottomButton = (Button)window.FindName("btnLogScrollBottom");

                topButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                followTail.IsChecked.Should().BeFalse();

                bottomButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                scrollViewer.VerticalOffset.Should().BeGreaterThanOrEqualTo(
                    scrollViewer.ScrollableHeight - LogViewScrollState.TailThresholdPixels);
                followTail.IsChecked.Should().BeTrue(
                    "reaching the tail restores live-follow mode regardless of whether the user dragged or pressed the bottom button");
            }
        });
    }

    [Fact]
    public void Scrollbar_thumb_reaching_tail_checks_follow_tail()
    {
        WpfStaTestHost.Run(() =>
        {
            var ctx = CreateShownLogPageContext(out var window, out var scrollViewer, out _);
            using (ctx)
            {
                var followTail = (CheckBox)window.FindName("chkLogFollowTail");
                var topButton = (Button)window.FindName("btnLogScrollTop");
                topButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                var scrollBar = FindScrollBar(scrollViewer, "PART_VerticalScrollBar");
                scrollBar.ApplyTemplate();
                var track = scrollBar.Template?.FindName("PART_Track", scrollBar) as Track;
                track.Should().NotBeNull();
                RaiseGenuineDrag(window, track!.Thumb, horizontalDelta: 0, verticalDelta: 40, steps: 8);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                LogViewScrollState.IsAtTail(scrollViewer).Should().BeTrue();
                followTail.IsChecked.Should().BeTrue(
                    "dragging the vertical thumb down to the tail must restore live-follow mode");
            }
        });
    }

    [Fact]
    public void Scrollbar_track_page_up_cancels_follow_tail()
    {
        WpfStaTestHost.Run(() =>
        {
            var ctx = CreateShownLogPageContext(out var window, out var scrollViewer, out _);
            using (ctx)
            {
                var followTail = (CheckBox)window.FindName("chkLogFollowTail");
                followTail.IsChecked = true;
                scrollViewer.ScrollableHeight.Should().BeGreaterThan(10);

                var scrollBar = FindScrollBar(scrollViewer, "PART_VerticalScrollBar");
                scrollBar.RaiseEvent(new ScrollEventArgs(
                    ScrollEventType.LargeDecrement,
                    Math.Max(0, scrollViewer.VerticalOffset - scrollViewer.ViewportHeight))
                {
                    RoutedEvent = ScrollBar.ScrollEvent,
                });
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                followTail.IsChecked.Should().BeFalse(
                    "clicking the scrollbar track above the thumb pages up into history and must cancel follow-tail");
            }
        });
    }

    [Fact]
    public void Refresh_with_stale_checked_follow_tail_keeps_user_position_and_unchecks()
    {
        WpfStaTestHost.Run(() =>
        {
            var ctx = CreateShownLogPageContext(out var window, out var scrollViewer, out var controller);
            using (ctx)
            {
                var followTail = (CheckBox)window.FindName("chkLogFollowTail");
                followTail.IsChecked = true;
                var viewport = GetLogViewport(controller);

                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller, window);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                viewport.ScrollToTopImmediate();
                viewport.PendingInitialTailScroll = true;
                viewport.ApplyFollowLatestIfEnabled();
                viewport.SuppressFollowAutoSync.Should().BeTrue(
                    "the regression requires a stale checked state inside programmatic-scroll suppression");
                scrollViewer.ScrollToVerticalOffset(0);
                window.UpdateLayout();
                scrollViewer.VerticalOffset.Should().Be(0);
                followTail.IsChecked.Should().BeTrue(
                    "scroll events muted during suppression must leave the checkbox stale-checked to set up the regression");

                var host = GetLogPageHost(controller);
                host.RefreshLogView(forceRedraw: true);
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller, window);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                scrollViewer.VerticalOffset.Should().BeLessThan(
                    scrollViewer.ScrollableHeight - LogViewScrollState.TailThresholdPixels,
                    "a refresh must never snap the viewport back to the tail while the user is reading history");
                followTail.IsChecked.Should().BeFalse(
                    "the refresh must self-heal the stale checked state instead of obeying it");
            }
        });
    }

    private static LogViewportController GetLogViewport(SettingsWindowController controller)
    {
        var host = GetLogPageHost(controller);
        var field = typeof(SettingsLogPageHost).GetField(
            "_logViewport",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (LogViewportController)field!.GetValue(host)!;
    }

    private static SettingsLogPageHost GetLogPageHost(SettingsWindowController controller)
    {
        var hostField = typeof(SettingsWindowController).GetField(
            "_logPageHost",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (SettingsLogPageHost)hostField!.GetValue(controller)!;
    }

    private static ScrollBar FindScrollBar(ScrollViewer scrollViewer, string partName)
    {
        scrollViewer.ApplyTemplate();
        var scrollBar = scrollViewer.Template?.FindName(partName, scrollViewer) as ScrollBar;
        scrollBar.Should().NotBeNull();
        return scrollBar!;
    }

    private static void RaiseGenuineDrag(
        Window window,
        Thumb thumb,
        double horizontalDelta,
        double verticalDelta,
        int steps)
    {
        thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        for (var i = 0; i < steps; i++)
        {
            thumb.RaiseEvent(new DragDeltaEventArgs(horizontalDelta, verticalDelta)
            {
                RoutedEvent = Thumb.DragDeltaEvent,
            });
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        thumb.RaiseEvent(new DragCompletedEventArgs(
            horizontalDelta * steps,
            verticalDelta * steps,
            canceled: false)
        {
            RoutedEvent = Thumb.DragCompletedEvent,
        });
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static Scope CreateShownLogPageContext(
        out Window window,
        out ScrollViewer scrollViewer,
        out SettingsWindowController controller)
    {
        var installRoot = Path.Combine(Path.GetTempPath(), "sg-followcancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(Path.Combine(installRoot, "bin"));
        var log = new System.Text.StringBuilder();
        for (var i = 0; i < 180; i++)
            log.AppendLine($"[INFO] 2026-07-04 08:{i % 60:D2}:{i % 60:D2} line {i}");
        File.WriteAllText(Path.Combine(installRoot, "SmartGuard.log"), log.ToString());

        var configPath = Path.Combine(installRoot, "SmartGuard.config.json");
        File.WriteAllText(
            configPath,
            "{\"BalancedThresholdSec\":300,\"PowerSaverThresholdSec\":900,\"LowBatteryPercent\":25,\"CheckIntervalSec\":30,\"BrightnessRestoreMs\":1000,\"ThemeFollowSystem\":false,\"ThemeIsDark\":false}");
        var repository = new SmartGuard.Configuration.GuardConfigRepository(configPath);
        var config = repository.LoadOrDefault(installRoot);
        controller = SettingsWindowController.TryCreate(installRoot, repository, config)!;
        controller.Should().NotBeNull();

        var field = typeof(SettingsWindowController).GetField("_window", BindingFlags.NonPublic | BindingFlags.Instance);
        window = (Window)field!.GetValue(controller)!;
        window.Width = 420;
        window.Height = 360;
        WpfStaTestHost.ShowAndWait(window);
        controller!.NavigateTo("logs");
        window.UpdateLayout();
        controller.EnsureLogScrollViewerHooked();
        SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller, window);

        scrollViewer = GetLogScrollViewer(controller);
        return new Scope(installRoot);
    }

    private static ScrollViewer GetLogScrollViewer(SettingsWindowController controller)
    {
        var host = GetLogPageHost(controller);
        var scrollViewerField = typeof(SettingsLogPageHost).GetField(
            "_logScrollViewer",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var scrollViewer = scrollViewerField!.GetValue(host) as ScrollViewer;
        scrollViewer.Should().NotBeNull();
        return scrollViewer!;
    }

    private sealed class Scope : IDisposable
    {
        private readonly string _root;
        public Scope(string root) => _root = root;
        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }
    }
}
