using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class LogHorizontalThumbRealMouseTests
{
    [Fact]
    public void Log_refresh_preserves_horizontal_offset_so_thumb_does_not_snap_back()
    {
        WpfStaTestHost.Run(() =>
        {
            var ctx = CreateShownLogPageContext(out var window, out var scrollViewer, out var controller);
            using (ctx)
            {
                scrollViewer.ScrollableWidth.Should().BeGreaterThan(0);

                var target = Math.Min(scrollViewer.ScrollableWidth, 80);
                scrollViewer.ScrollToHorizontalOffset(target);
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                scrollViewer.HorizontalOffset.Should().BeGreaterThan(10);

                controller.ForceRefreshLogView();
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller, window);
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                scrollViewer.HorizontalOffset.Should().BeGreaterThan(
                    10,
                    "horizontal offset must be preserved across log refreshes so the thumb does not snap back");
            }
        });
    }

    [Fact]
    public void Horizontal_thumb_track_survives_end_scroll_default_value()
    {
        WpfStaTestHost.Run(() =>
        {
            var ctx = CreateShownLogPageContext(out var window, out var scrollViewer, out _);
            using (ctx)
            {
                var scrollBar = FindScrollBar(scrollViewer, "PART_HorizontalScrollBar");
                scrollBar.ApplyTemplate();
                var track = scrollBar.Template?.FindName("PART_Track", scrollBar) as Track;
                track.Should().NotBeNull();

                RaiseGenuineDrag(window, track!.Thumb, horizontalDelta: 30, verticalDelta: 0, steps: 4);
                var target = scrollViewer.HorizontalOffset;
                target.Should().BeGreaterThan(10);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                scrollViewer.HorizontalOffset.Should().BeApproximately(
                    target,
                    1,
                    "EndScroll may report its default value but must not reset the committed thumb position");
            }
        });
    }

    [Fact]
    public void Horizontal_thumb_drag_survives_follow_tail_refresh()
    {
        WpfStaTestHost.Run(() =>
        {
            var ctx = CreateShownLogPageContext(out var window, out var scrollViewer, out var controller);
            using (ctx)
            {
                var scrollBar = FindScrollBar(scrollViewer, "PART_HorizontalScrollBar");
                scrollBar.ApplyTemplate();
                var track = scrollBar.Template?.FindName("PART_Track", scrollBar) as Track;
                track.Should().NotBeNull();

                RaiseGenuineDrag(window, track!.Thumb, horizontalDelta: 30, verticalDelta: 0, steps: 4);
                var target = scrollViewer.HorizontalOffset;
                target.Should().BeGreaterThan(10);

                scrollViewer.ScrollToHorizontalOffset(0);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                scrollViewer.HorizontalOffset.Should().BeApproximately(
                    0,
                    1,
                    "the delayed native/default reset observed in the real window must be part of the regression");

                controller.ForceRefreshLogView();
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller, window);
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                scrollViewer.HorizontalOffset.Should().BeApproximately(
                    target,
                    1,
                    "refresh must restore the last committed user drag rather than preserving a delayed default zero");
            }
        });
    }

    [Fact]
    public void Vertical_thumb_track_disables_follow_tail_and_survives_end_scroll()
    {
        WpfStaTestHost.Run(() =>
        {
            var ctx = CreateShownLogPageContext(out var window, out var scrollViewer, out var controller);
            using (ctx)
            {
                scrollViewer.ScrollableHeight.Should().BeGreaterThan(10);
                var followTail = window.FindName("chkLogFollowTail") as CheckBox;
                followTail.Should().NotBeNull();
                followTail!.IsChecked = true;

                var scrollBar = FindScrollBar(scrollViewer, "PART_VerticalScrollBar");
                scrollBar.ApplyTemplate();
                var track = scrollBar.Template?.FindName("PART_Track", scrollBar) as Track;
                track.Should().NotBeNull();

                RaiseGenuineDrag(window, track!.Thumb, horizontalDelta: 0, verticalDelta: -20, steps: 8);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                followTail.IsChecked.Should().BeFalse(
                    "manual vertical thumb movement must take precedence over follow-tail");
                scrollViewer.VerticalOffset.Should().BeLessThan(
                    scrollViewer.ScrollableHeight - LogViewScrollState.TailThresholdPixels,
                    "EndScroll must not return the log viewport to its default position");
            }
        });
    }

    [Fact]
    public void Scrolling_to_tail_keeps_user_horizontal_offset()
    {
        WpfStaTestHost.Run(() =>
        {
            var ctx = CreateShownLogPageContext(out var window, out var scrollViewer, out var controller);
            using (ctx)
            {
                var target = Math.Min(scrollViewer.ScrollableWidth, 80);
                scrollViewer.ScrollToHorizontalOffset(target);
                window.UpdateLayout();
                scrollViewer.HorizontalOffset.Should().BeGreaterThan(10);

                // Follow-tail is a vertical concern; jumping to the newest log
                // line must not snap the user's horizontal position back to 0
                // (ScrollIntoView aligns the item's left edge otherwise).
                InvokeScrollToBottom(controller);
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                scrollViewer.VerticalOffset.Should().BeApproximately(
                    scrollViewer.ScrollableHeight,
                    1,
                    "scroll-to-bottom must still reach the tail vertically");
                scrollViewer.HorizontalOffset.Should().BeGreaterThan(
                    10,
                    "scrolling to the tail must not reset the user's horizontal offset");
            }
        });
    }

    private static void InvokeScrollToBottom(SettingsWindowController controller)
    {
        var hostField = typeof(SettingsWindowController).GetField(
            "_logPageHost",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var host = hostField!.GetValue(controller)!;
        var viewportField = host.GetType().GetField(
            "_logViewport",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var viewport = viewportField!.GetValue(host)!;
        var method = viewport.GetType().GetMethod(
            "ScrollToBottom",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method!.Invoke(viewport, null);
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

    private static Scope CreateShownLogPageContext(out Window window, out ScrollViewer scrollViewer, out SettingsWindowController controller)
    {
        var installRoot = Path.Combine(Path.GetTempPath(), "sg-realmouse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(Path.Combine(installRoot, "bin"));
        var log = new System.Text.StringBuilder();
        for (var i = 0; i < 180; i++)
            log.AppendLine($"[INFO] 2026-07-04 08:{i % 60:D2}:{i % 60:D2} line {i} " + new string('x', 600));
        File.WriteAllText(
            Path.Combine(installRoot, "SmartGuard.log"),
            log.ToString());

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

        scrollViewer = GetLogScrollViewer(window, controller);
        return new Scope(installRoot);
    }

    private static ScrollViewer GetLogScrollViewer(Window window, SettingsWindowController controller)
    {
        var hostField = typeof(SettingsWindowController).GetField(
            "_logPageHost",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var host = hostField!.GetValue(controller)!;
        var scrollViewerField = host.GetType().GetField(
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
