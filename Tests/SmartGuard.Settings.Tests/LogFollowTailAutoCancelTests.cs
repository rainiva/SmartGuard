using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

// Regression for the follow-tail auto-cancel bug: scrolling up sometimes
// failed to uncheck 跟随最新, so the next refresh snapped the viewport back
// to the tail while the user was reading history. Root causes:
// 1) mouse wheel scrolling only reaches OnScrollChanged, which is muted
//    during the post-refresh SuppressFollowTailAutoSync window;
// 2) scrollbar track paging (LargeDecrement) was never treated as a user
//    gesture;
// 3) the refresh pipeline re-applied tail scrolling whenever the checkbox
//    was still (stale) checked, ignoring the user's actual position.
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
                var coordinator = GetFollowTailCoordinator(controller);

                // Open the post-refresh suppression window exactly like the
                // 2s auto-refresh does, then wheel up inside it.
                coordinator.ApplyScrollIfEnabled();
                coordinator.SuppressFollowTailAutoSync.Should().BeTrue(
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
                var coordinator = GetFollowTailCoordinator(controller);

                // Let the page-activation initial scroll fully settle first.
                WpfStaTestHost.WaitUntil(
                    () => !coordinator.PendingFollowTailInitialScroll && !coordinator.SuppressFollowTailAutoSync,
                    TimeSpan.FromSeconds(10),
                    "the initial follow-tail scroll to settle");

                // Reproduce the stale state through the bug mechanism itself:
                // scrolling up inside the programmatic-scroll suppression
                // window (here opened by ScrollToBottom) is muted, so the
                // checkbox stays checked while the user sits at the top.
                coordinator.ScrollToBottom();
                scrollViewer.ScrollToVerticalOffset(0);
                window.UpdateLayout();
                scrollViewer.VerticalOffset.Should().Be(0);
                followTail.IsChecked.Should().BeTrue(
                    "the muted scroll event must leave the checkbox stale-checked to set up the regression");

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

    private static SettingsLogFollowTailCoordinator GetFollowTailCoordinator(SettingsWindowController controller)
    {
        var host = GetLogPageHost(controller);
        var field = typeof(SettingsLogPageHost).GetField(
            "_followTailCoordinator",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (SettingsLogFollowTailCoordinator)field!.GetValue(host)!;
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
