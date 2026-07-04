using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

/// <summary>
/// 诊断测试:不预设 ScrollBar.Value、不手工 Raise Scroll 事件,
/// 只注入真实的 Thumb Drag 事件链(DragStarted → DragDelta → DragCompleted),
/// 让 ScrollBar 自己走 Track.ValueFromDistance → UpdateValue → Scroll 事件。
/// 这是对既有测试(预设 Value 后手工 Raise Scroll)的反证补强。
/// </summary>
[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class ScrollBarRealDragDiagnosticTests
{
    [Fact]
    public void Genuine_thumb_drag_events_move_about_page_vertical_content()
    {
        RunOnSta(() =>
        {
            using var ctx = CreateShownAboutPageContext(out var window, out var scrollViewer);
            try
            {
                scrollViewer.ScrollableHeight.Should().BeGreaterThan(0);
                var scrollBar = FindScrollBar(scrollViewer, "PART_VerticalScrollBar");
                scrollBar.Should().NotBeNull();
                scrollBar!.Maximum.Should().BeGreaterThan(0, "Maximum 必须绑定到 ScrollableHeight,否则 Density=0 拖拽死");

                var thumb = GetThumb(scrollBar);
                scrollViewer.VerticalOffset.Should().BeApproximately(0, 1.0);

                RaiseGenuineDrag(window, thumb, horizontalDelta: 0, verticalDelta: 30, steps: 4);

                scrollViewer.VerticalOffset.Should().BeGreaterThan(
                    10,
                    "真实 DragDelta 事件链应驱动 ScrollBar.UpdateValue 并滚动内容");
            }
            finally
            {
                ctx.Dispose();
            }
        });
    }

    [Fact]
    public void Genuine_thumb_drag_events_move_log_page_horizontal_content()
    {
        RunOnSta(() =>
        {
            using var ctx = CreateShownLogPageContext(out var window, out var scrollViewer);
            try
            {
                scrollViewer.ScrollableWidth.Should().BeGreaterThan(0);
                ScrollBarAutoHide.NotifyScrollActivity(scrollViewer);

                var scrollBar = FindScrollBar(scrollViewer, "PART_HorizontalScrollBar");
                scrollBar.Should().NotBeNull();
                scrollBar!.Maximum.Should().BeGreaterThan(0, "Maximum 必须绑定到 ScrollableWidth,否则 Density=0 拖拽死");

                var thumb = GetThumb(scrollBar);
                scrollViewer.HorizontalOffset.Should().BeApproximately(0, 1.0);

                RaiseGenuineDrag(window, thumb, horizontalDelta: 30, verticalDelta: 0, steps: 4);

                scrollViewer.HorizontalOffset.Should().BeGreaterThan(
                    10,
                    "真实 DragDelta 事件链应驱动 ScrollBar.UpdateValue 并滚动内容");
            }
            finally
            {
                ctx.Dispose();
            }
        });
    }

    [Fact]
    public void Genuine_thumb_drag_events_move_log_page_vertical_content()
    {
        RunOnSta(() =>
        {
            using var ctx = CreateShownLogPageContext(out var window, out var scrollViewer, manyLines: true);
            try
            {
                scrollViewer.ScrollableHeight.Should().BeGreaterThan(0);
                ScrollBarAutoHide.NotifyScrollActivity(scrollViewer);

                var followTail = window.FindName("chkLogFollowTail") as CheckBox;
                followTail!.IsChecked = false;
                scrollViewer.ScrollToVerticalOffset(0);
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                var scrollBar = FindScrollBar(scrollViewer, "PART_VerticalScrollBar");
                scrollBar.Should().NotBeNull();
                scrollBar!.Maximum.Should().BeGreaterThan(0);

                var thumb = GetThumb(scrollBar);
                var start = scrollViewer.VerticalOffset;

                RaiseGenuineDrag(window, thumb, horizontalDelta: 0, verticalDelta: 30, steps: 4);

                scrollViewer.VerticalOffset.Should().BeGreaterThan(
                    start + 5,
                    "真实 DragDelta 事件链应驱动日志垂直滚动");
            }
            finally
            {
                ctx.Dispose();
            }
        });
    }

    [Fact]
    public void Scrollbar_track_click_pages_content_because_track_has_repeat_buttons()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

        // 原生 ScrollBar 模板依赖 Track.DecreaseRepeatButton / IncreaseRepeatButton
        // 响应「点击轨道空白处翻页」。缺失时点击轨道完全无响应。
        xaml.Should().MatchRegex(
            "x:Key=\"SettingsScrollBar\"[\\s\\S]{0,2400}Track\\.DecreaseRepeatButton",
            "SettingsScrollBar 的 Track 必须包含 DecreaseRepeatButton,否则点击轨道上方/左方无响应");
        xaml.Should().MatchRegex(
            "x:Key=\"SettingsScrollBar\"[\\s\\S]{0,3200}Track\\.IncreaseRepeatButton",
            "SettingsScrollBar 的 Track 必须包含 IncreaseRepeatButton,否则点击轨道下方/右方无响应");
    }

    private static void RaiseGenuineDrag(Window window, Thumb thumb, double horizontalDelta, double verticalDelta, int steps)
    {
        thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        Pump(window);
        for (var i = 0; i < steps; i++)
        {
            thumb.RaiseEvent(new DragDeltaEventArgs(horizontalDelta, verticalDelta)
            {
                RoutedEvent = Thumb.DragDeltaEvent,
            });
            Pump(window);
        }

        thumb.RaiseEvent(new DragCompletedEventArgs(horizontalDelta * steps, verticalDelta * steps, false)
        {
            RoutedEvent = Thumb.DragCompletedEvent,
        });
        Pump(window);
    }

    private static void Pump(Window window)
        => window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

    private static Thumb GetThumb(ScrollBar scrollBar)
    {
        scrollBar.ApplyTemplate();
        var track = scrollBar.Template?.FindName("PART_Track", scrollBar) as Track;
        track.Should().NotBeNull();
        track!.Thumb.Should().NotBeNull();
        return track.Thumb;
    }

    private static ScrollBar? FindScrollBar(ScrollViewer scrollViewer, string partName)
    {
        scrollViewer.ApplyTemplate();
        return scrollViewer.Template?.FindName(partName, scrollViewer) as ScrollBar;
    }

    private static IDisposable CreateShownAboutPageContext(out Window window, out ScrollViewer scrollViewer)
    {
        var ctx = CreateControllerContext(out var controller, out window);
        window.Width = 760;
        window.Height = 520;
        WpfStaTestHost.ShowAndWait(window);
        controller!.NavigateTo("about");
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        scrollViewer = (window.FindName("contentScrollViewer") as ScrollViewer)!;
        scrollViewer.Should().NotBeNull();
        return ctx;
    }

    private static IDisposable CreateShownLogPageContext(
        out Window window,
        out ScrollViewer scrollViewer,
        bool manyLines = false)
    {
        var logContent = manyLines
            ? BuildManyLines()
            : "[INFO] 2026-07-04 09:00:00 START-" + new string('x', 500) + "-END";
        var ctx = CreateControllerContext(out var controller, out window, logContent);
        window.Width = manyLines ? 760 : 360;
        window.Height = 520;
        WpfStaTestHost.ShowAndWait(window);
        controller!.NavigateTo("logs");
        window.UpdateLayout();
        controller.EnsureLogScrollViewerHooked();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        scrollViewer = GetLogScrollViewer(window, controller);
        return ctx;
    }

    private static string BuildManyLines()
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < 180; i++)
            builder.AppendLine($"[INFO] 2026-07-04 09:{i % 60:D2}:{i % 60:D2} scroll line {i}");
        return builder.ToString();
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

    private static ControllerContext CreateControllerContext(
        out SettingsWindowController? controller,
        out Window window,
        string? logContent = null)
    {
        WpfStaTestHost.EnsureApplication();
        var installRoot = Path.Combine(Path.GetTempPath(), "sg-scroll-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(Path.Combine(installRoot, "bin"));
        if (logContent is not null)
            File.WriteAllText(Path.Combine(installRoot, "SmartGuard.log"), logContent);

        var configPath = Path.Combine(installRoot, "SmartGuard.config.json");
        File.WriteAllText(
            configPath,
            "{\"BalancedThresholdSec\":300,\"PowerSaverThresholdSec\":900,\"LowBatteryPercent\":25,\"CheckIntervalSec\":30,\"BrightnessRestoreMs\":1000,\"ThemeFollowSystem\":false,\"ThemeIsDark\":false}");
        var repository = new GuardConfigRepository(configPath);
        var config = repository.LoadOrDefault(installRoot);
        controller = SettingsWindowController.TryCreate(installRoot, repository, config);
        controller.Should().NotBeNull();

        var field = typeof(SettingsWindowController).GetField("_window", BindingFlags.NonPublic | BindingFlags.Instance);
        window = (Window)field!.GetValue(controller)!;
        return new ControllerContext(installRoot, window);
    }

    private static void RunOnSta(Action action) => WpfStaTestHost.Run(action);

    private sealed class ControllerContext : IDisposable
    {
        private readonly string _installRoot;

        internal ControllerContext(string installRoot, Window window)
        {
            _installRoot = installRoot;
            Window = window;
        }

        internal Window Window { get; }

        public void Dispose()
        {
            try { Window.Close(); } catch { }
            try { Directory.Delete(_installRoot, true); } catch { }
        }
    }
}
