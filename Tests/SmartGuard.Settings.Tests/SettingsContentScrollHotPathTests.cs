using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class SettingsContentScrollHotPathTests
{
    [Fact]
    public void About_page_vertical_scrollbar_is_hit_testable()
    {
        RunOnSta(() =>
        {
            using var ctx = CreateShownAboutPageContext(out var window, out var scrollViewer);
            try
            {
                scrollViewer.ScrollableHeight.Should().BeGreaterThan(0);
                var verticalScrollBar = FindVerticalScrollBar(scrollViewer);
                verticalScrollBar.Should().NotBeNull();
                verticalScrollBar!.IsHitTestVisible.Should().BeTrue();
                verticalScrollBar.IsEnabled.Should().BeTrue();
                verticalScrollBar.ActualWidth.Should().BeGreaterThanOrEqualTo(8);

                var hit = HitTestScrollBarThumb(window, verticalScrollBar);
                hit.Should().NotBeNull("the vertical scrollbar thumb should accept hit tests");
            }
            finally
            {
                ctx.Dispose();
            }
        });
    }

    [Fact]
    public void User_dragging_about_page_scrollbar_moves_content()
    {
        RunOnSta(() =>
        {
            using var ctx = CreateShownAboutPageContext(out var window, out var scrollViewer);
            try
            {
                scrollViewer.ScrollableHeight.Should().BeGreaterThan(0);
                var verticalScrollBar = FindVerticalScrollBar(scrollViewer);
                verticalScrollBar.Should().NotBeNull();

                scrollViewer.VerticalOffset.Should().BeApproximately(0, 1.0);
                DragScrollBarToFraction(window, scrollViewer, verticalScrollBar!, 0.75);

                scrollViewer.VerticalOffset.Should().BeGreaterThan(
                    10,
                    "dragging the about page scrollbar should move the content");
            }
            finally
            {
                ctx.Dispose();
            }
        });
    }

    [Fact]
    public void Content_scrollbar_drag_works_on_general_and_advanced_pages()
    {
        RunOnSta(() =>
        {
            using var ctx = CreateControllerContext(out var controller, out var window);
            try
            {
                window.Width = 760;
                window.Height = 520;
                WpfStaTestHost.ShowAndWait(window);

                foreach (var page in new[] { "general", "advanced" })
                {
                    controller!.NavigateTo(page);
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    var scrollViewer = window.FindName("contentScrollViewer") as ScrollViewer;
                    scrollViewer.Should().NotBeNull();
                    scrollViewer!.ScrollableHeight.Should().BeGreaterThan(0);

                    var verticalScrollBar = FindVerticalScrollBar(scrollViewer);
                    verticalScrollBar.Should().NotBeNull();
                    verticalScrollBar!.IsHitTestVisible.Should().BeTrue();

                    DragScrollBarToFraction(window, scrollViewer, verticalScrollBar, 0.75);

                    scrollViewer.VerticalOffset.Should().BeGreaterThan(
                        10,
                        $"dragging the scrollbar should move the {page} page content");
                }
            }
            finally
            {
                ctx.Dispose();
            }
        });
    }

    [Fact]
    public void Log_page_horizontal_scrollbar_is_hit_testable_when_content_overflows()
    {
        RunOnSta(() =>
        {
            using var ctx = CreateShownLogPageContext(out var window, out var scrollViewer, wideLine: true);
            try
            {
                scrollViewer.ScrollableWidth.Should().BeGreaterThan(0);
                ScrollBarAutoHide.NotifyScrollActivity(scrollViewer);

                var horizontalScrollBar = FindHorizontalScrollBar(scrollViewer);
                horizontalScrollBar.Should().NotBeNull();
                horizontalScrollBar!.IsHitTestVisible.Should().BeTrue();
                horizontalScrollBar.IsEnabled.Should().BeTrue();
                horizontalScrollBar.ActualHeight.Should().BeGreaterThanOrEqualTo(8);

                var hit = HitTestScrollBarThumb(window, horizontalScrollBar);
                hit.Should().NotBeNull("the horizontal scrollbar thumb should accept hit tests");
            }
            finally
            {
                ctx.Dispose();
            }
        });
    }

    [Fact]
    public void Log_scroll_viewer_horizontal_bar_uses_native_offset_binding()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

        xaml.Should().MatchRegex(
            "x:Name=\"PART_HorizontalScrollBar\"[\\s\\S]{0,400}Value=\"\\{TemplateBinding HorizontalOffset\\}\"",
            "horizontal scrollbar value should use native TemplateBinding like vertical");
    }

    [Fact]
    public void User_dragging_log_page_horizontal_scrollbar_thumb_moves_content()
    {
        RunOnSta(() =>
        {
            using var ctx = CreateShownLogPageContext(out var window, out var scrollViewer, wideLine: true);
            try
            {
                scrollViewer.ScrollableWidth.Should().BeGreaterThan(0);
                ScrollBarAutoHide.NotifyScrollActivity(scrollViewer);

                var horizontalScrollBar = FindHorizontalScrollBar(scrollViewer);
                horizontalScrollBar.Should().NotBeNull();

                scrollViewer.HorizontalOffset.Should().BeApproximately(0, 1.0);
                DragHorizontalThumb(window, scrollViewer, horizontalScrollBar!, delta: 80);

                scrollViewer.HorizontalOffset.Should().BeGreaterThan(
                    10,
                    "dragging the horizontal thumb should move the log content");
            }
            finally
            {
                ctx.Dispose();
            }
        });
    }

    [Fact]
    public void User_dragging_log_page_horizontal_scrollbar_moves_content()
    {
        RunOnSta(() =>
        {
            using var ctx = CreateShownLogPageContext(out var window, out var scrollViewer, wideLine: true);
            try
            {
                scrollViewer.ScrollableWidth.Should().BeGreaterThan(0);
                ScrollBarAutoHide.NotifyScrollActivity(scrollViewer);

                var horizontalScrollBar = FindHorizontalScrollBar(scrollViewer);
                horizontalScrollBar.Should().NotBeNull();

                scrollViewer.HorizontalOffset.Should().BeApproximately(0, 1.0);
                DragScrollBarToFraction(window, scrollViewer, horizontalScrollBar!, 0.75, horizontal: true);

                scrollViewer.HorizontalOffset.Should().BeGreaterThan(
                    10,
                    "dragging the horizontal log scrollbar should move the content");
            }
            finally
            {
                ctx.Dispose();
            }
        });
    }

    [Fact]
    public void User_dragging_log_page_vertical_scrollbar_moves_content()
    {
        RunOnSta(() =>
        {
            using var ctx = CreateShownLogPageContext(out var window, out var scrollViewer, wideLine: false, manyLines: true);
            try
            {
                scrollViewer.ScrollableHeight.Should().BeGreaterThan(0);
                ScrollBarAutoHide.NotifyScrollActivity(scrollViewer);

                var followTail = window.FindName("chkLogFollowTail") as CheckBox;
                followTail.Should().NotBeNull();
                scrollViewer.ScrollToVerticalOffset(0);
                followTail!.IsChecked = false;
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                var verticalScrollBar = FindVerticalScrollBar(scrollViewer);
                verticalScrollBar.Should().NotBeNull();

                scrollViewer.VerticalOffset.Should().BeApproximately(0, 1.0);
                var startOffset = scrollViewer.VerticalOffset;
                DragScrollBarToFraction(window, scrollViewer, verticalScrollBar!, 0.75);

                scrollViewer.VerticalOffset.Should().BeGreaterThan(
                    startOffset + 10,
                    "dragging the vertical log scrollbar should move the content");
            }
            finally
            {
                ctx.Dispose();
            }
        });
    }

    [Fact]
    public void Wheel_scroll_on_about_page_advances_offset_monotonically()
    {
        RunOnSta(() =>
        {
            using var ctx = CreateShownAboutPageContext(out _, out var scrollViewer);
            try
            {
                var previous = scrollViewer.VerticalOffset;
                for (var i = 0; i < 100; i++)
                {
                    scrollViewer.LineDown();
                    scrollViewer.VerticalOffset.Should().BeGreaterThanOrEqualTo(previous - 0.01);
                    previous = scrollViewer.VerticalOffset;
                }
            }
            finally
            {
                ctx.Dispose();
            }
        });
    }

    [Fact]
    public void Wheel_scroll_on_about_page_keeps_scrollable_height_stable()
    {
        RunOnSta(() =>
        {
            using var ctx = CreateShownAboutPageContext(out _, out var scrollViewer);
            try
            {
                var expectedHeight = scrollViewer.ScrollableHeight;
                expectedHeight.Should().BeGreaterThan(0);

                for (var i = 0; i < 100; i++)
                    scrollViewer.LineDown();

                scrollViewer.ScrollableHeight.Should().BeApproximately(expectedHeight, 1.0);
            }
            finally
            {
                ctx.Dispose();
            }
        });
    }

    [Fact]
    public void Content_scroll_viewer_uses_content_scroll_style_without_thumb_sync()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

        xaml.Should().MatchRegex(
            "x:Name=\"contentScrollViewer\"[\\s\\S]{0,320}Style=\"\\{StaticResource SettingsContentScrollViewer\\}\"",
            "content pages should use the dedicated content scroll viewer style");
        xaml.Should().NotMatchRegex(
            "x:Name=\"contentScrollViewer\"[\\s\\S]{0,320}Style=\"\\{StaticResource SettingsScrollViewer\\}\"",
            "content pages should not use the log scroll viewer style directly");
    }

    [Fact]
    public void Log_list_scroll_viewer_uses_styled_scrollbar_parts()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

        xaml.Should().Contain("x:Name=\"PART_VerticalScrollBar\"");
        xaml.Should().Contain("x:Name=\"PART_HorizontalScrollBar\"");
        xaml.Should().MatchRegex(
            "x:Key=\"SettingsScrollViewer\"[\\s\\S]{0,1800}Style=\"\\{StaticResource SettingsScrollBar\\}\"",
            "styled scroll viewer template should style both scrollbar parts");
        xaml.Should().MatchRegex(
            "x:Key=\"SettingsLogListBox\"[\\s\\S]{0,900}Style=\"\\{StaticResource SettingsScrollViewer\\}\"",
            "log list should host the styled scroll viewer template");
    }

    [Fact]
    public void Settings_window_controller_does_not_attach_thumb_sync()
    {
        var source = File.ReadAllText(Path.Combine(
            SettingsSourcePaths.RepoRoot(),
            "src",
            "SmartGuard.Settings",
            "SettingsWindowController.cs"));

        source.Should().NotContain("ScrollViewerThumbSync");
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
        bool wideLine,
        bool manyLines = false)
    {
        var ctx = CreateControllerContext(out var controller, out window, logContent: BuildLogContent(wideLine, manyLines));
        window.Width = wideLine ? 360 : 760;
        window.Height = 520;
        WpfStaTestHost.ShowAndWait(window);
        controller!.NavigateTo("logs");
        window.UpdateLayout();
        controller.EnsureLogScrollViewerHooked();
        SettingsLogNavResponsivenessTests.WaitUntilLogContent(window);

        scrollViewer = GetLogScrollViewer(window, controller);
        return ctx;
    }

    private static string BuildLogContent(bool wideLine, bool manyLines)
    {
        if (manyLines)
        {
            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < 180; i++)
                builder.AppendLine($"[INFO] 2026-07-04 09:{i % 60:D2}:{i % 60:D2} scroll line {i}");
            return builder.ToString();
        }

        return wideLine
            ? "[INFO] 2026-07-04 09:00:00 START-" + new string('x', 500) + "-END"
            : "[INFO] 2026-07-04 09:00:00 ok\n";
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
        var installRoot = Path.Combine(Path.GetTempPath(), "sg-scroll-hotpath-" + Guid.NewGuid().ToString("N"));
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

    private static ScrollBar? FindVerticalScrollBar(ScrollViewer scrollViewer)
    {
        scrollViewer.ApplyTemplate();
        return scrollViewer.Template?.FindName("PART_VerticalScrollBar", scrollViewer) as ScrollBar;
    }

    private static ScrollBar? FindHorizontalScrollBar(ScrollViewer scrollViewer)
    {
        scrollViewer.ApplyTemplate();
        return scrollViewer.Template?.FindName("PART_HorizontalScrollBar", scrollViewer) as ScrollBar;
    }

    private static void DragScrollBarToFraction(
        Window window,
        ScrollViewer scrollViewer,
        ScrollBar scrollBar,
        double fraction,
        bool horizontal = false)
    {
        scrollBar.ApplyTemplate();
        var track = scrollBar.Template?.FindName("PART_Track", scrollBar) as Track;
        track.Should().NotBeNull();
        var thumb = track!.Thumb;
        thumb.Should().NotBeNull();

        var delta = Math.Max(30, scrollBar.Maximum * fraction / 4);
        thumb!.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        Pump(window);
        for (var i = 0; i < 4; i++)
        {
            thumb.RaiseEvent(new DragDeltaEventArgs(
                horizontal ? delta : 0,
                horizontal ? 0 : delta)
            {
                RoutedEvent = Thumb.DragDeltaEvent,
            });
            Pump(window);
        }

        thumb.RaiseEvent(new DragCompletedEventArgs(
            horizontal ? delta * 4 : 0,
            horizontal ? 0 : delta * 4,
            false)
        {
            RoutedEvent = Thumb.DragCompletedEvent,
        });
        Pump(window);
    }

    private static void Pump(Window window)
        => window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

    private static void DragHorizontalThumb(Window window, ScrollViewer scrollViewer, ScrollBar scrollBar, double delta)
    {
        scrollBar.ApplyTemplate();
        var track = scrollBar.Template?.FindName("PART_Track", scrollBar) as Track;
        track.Should().NotBeNull();
        track!.ApplyTemplate();

        var thumb = track.Thumb;
        thumb.Should().NotBeNull();

        thumb!.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        Pump(window);
        for (var i = 0; i < 4; i++)
        {
            thumb.RaiseEvent(new DragDeltaEventArgs(delta, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            scrollViewer.UpdateLayout();
            Pump(window);
        }

        thumb.RaiseEvent(new DragCompletedEventArgs(delta * 4, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Pump(window);
    }

    private static DependencyObject? HitTestScrollBarThumb(Window window, ScrollBar scrollBar)
    {
        scrollBar.ApplyTemplate();
        var track = scrollBar.Template?.FindName("PART_Track", scrollBar) as Track;
        track.Should().NotBeNull();
        track!.ApplyTemplate();

        var thumb = track.Thumb;
        thumb.Should().NotBeNull();

        var point = thumb!.TranslatePoint(
            new Point(Math.Max(1, thumb.ActualWidth / 2), Math.Max(1, thumb.ActualHeight / 2)),
            window);
        return window.InputHitTest(point) as DependencyObject;
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
