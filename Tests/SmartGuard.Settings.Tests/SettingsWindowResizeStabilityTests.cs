using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class SettingsWindowResizeStabilityTests
{
    [Fact]
    public void BringToForeground_uses_topmost_flash_without_leaving_topmost_enabled()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var window = new Window();
            SettingsWindowPresentation.BringToForeground(window);
            window.Topmost.Should().BeFalse();
        });
    }

    [Fact]
    public void Content_pages_stretch_to_fill_resizable_window_area()
    {
        var xaml = File.ReadAllText(RepoXamlPath());
        xaml.Should().MatchRegex(
            "x:Name=\"contentScrollViewer\"[\\s\\S]{0,220}VerticalAlignment=\"Stretch\"",
            "outer scroll viewer should stretch when the window is resized");
        xaml.Should().MatchRegex(
            "x:Name=\"pageLogs\"[\\s\\S]{0,220}VerticalAlignment=\"Stretch\"",
            "logs page should stretch when the window is resized");
    }

    [Fact]
    public void Log_status_stays_within_window_bounds_when_high_dpi_layout_is_constrained()
    {
        RunOnSta(() =>
        {
            EnsureApplication();

            var installRoot = CreateInstallRoot();
            var lines = Enumerable.Range(1, 200)
                .Select(index => $"[INFO] 2026-08-07 10:00:{index % 60:00} Uninstall event {index}")
                .ToArray();
            File.WriteAllText(
                Path.Combine(installRoot, "SmartGuard.log"),
                string.Join(Environment.NewLine, lines),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            try
            {
                var controller = CreateController(installRoot);
                controller.Should().NotBeNull();

                var window = GetWindow(controller!);
                try
                {
                    window.Width = 800;
                    window.Height = 640;
                    WpfStaTestHost.ShowAndWait(window);
                    controller!.NavigateTo("logs");
                    window.UpdateLayout();
                    SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller, window);
                    WpfStaTestHost.WaitForRender();

                    var pageLogs = window.FindName("pageLogs") as FrameworkElement;
                    var status = window.FindName("lblLogStatus") as FrameworkElement;
                    pageLogs.Should().NotBeNull();
                    status.Should().NotBeNull();

                    var statusOrigin = status!.TranslatePoint(new Point(0, 0), window);
                    var statusRight = statusOrigin.X + status.ActualWidth;

                    statusRight.Should().BeLessThanOrEqualTo(
                        window.ActualWidth + 1.0,
                        "the status summary must remain reachable inside the window at high DPI");
                }
                finally
                {
                    window.Close();
                }
            }
            finally
            {
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void Layout_stability_is_scoped_to_visible_log_page()
    {
        var source = File.ReadAllText(RepoLayoutStabilityPath());

        source.Should().Contain("window.FindName(\"pageLogs\") is not FrameworkElement pageLogs");
        source.Should().Contain("pageLogs.Visibility != Visibility.Visible");
    }

    [Fact]
    public void User_maximizes_then_restores_settings_window_survives_layout_measure()
    {
        RunOnSta(() =>
        {
            EnsureApplication();

            var installRoot = CreateInstallRoot();
            try
            {
                var controller = CreateController(installRoot);
                controller.Should().NotBeNull();

                var window = GetWindow(controller);
                var act = () =>
                {
                    window.Width = 800;
                    window.Height = 640;
                    window.WindowState = WindowState.Maximized;
                    window.Measure(new Size(1600, 900));
                    window.Arrange(new Rect(0, 0, 1600, 900));

                    window.WindowState = WindowState.Normal;
                    window.Measure(new Size(800, 640));
                    window.Arrange(new Rect(0, 0, 800, 640));
                };

                act.Should().NotThrow();
            }
            finally
            {
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void User_maximizes_log_page_scroll_viewer_keeps_positive_viewport()
    {
        RunOnSta(() =>
        {
            EnsureApplication();

            var installRoot = CreateInstallRoot();
            File.WriteAllText(Path.Combine(installRoot, "SmartGuard.log"), "[INFO] 2026-06-21 10:00:00 ok\n");

            try
            {
                var controller = CreateController(installRoot);
                controller.Should().NotBeNull();

                var window = GetWindow(controller);
                try
                {
                    window.Width = 1200;
                    window.Height = 900;
                    WpfStaTestHost.ShowAndWait(window);
                    controller!.NavigateTo("logs");
                    window.UpdateLayout();
                    SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

                    SettingsWindowLayoutStability.StabilizeContentLayout(window);
                    window.UpdateLayout();

                    window.Dispatcher.Invoke(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    var txtPageTitle = window.FindName("txtPageTitle") as TextBlock;
                    txtPageTitle.Should().NotBeNull();
                    txtPageTitle!.Text.Should().Be("日志");

                    var pageLogs = window.FindName("pageLogs") as FrameworkElement;
                    pageLogs.Should().NotBeNull();
                    pageLogs!.Visibility.Should().Be(Visibility.Visible);

                    var lstLogView = window.FindName("lstLogView") as ListBox;
                    lstLogView.Should().NotBeNull();
                    lstLogView!.ActualHeight.Should().BeGreaterThan(100);

                    SettingsWindowLayoutStability.StabilizeContentLayout(window);
                    SettingsWindowLayoutStability.HandleWindowStateChanged(window, isDarkTheme: false, WindowState.Maximized);
                    window.WindowState = WindowState.Maximized;
                    window.UpdateLayout();

                    window.Dispatcher.Invoke(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    lstLogView.ActualHeight.Should().BeGreaterThan(100);
                }
                finally
                {
                    WindowTitleBarTheme.Apply(window, isDarkMode: false);
                    window.Close();
                }
            }
            finally
            {
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void User_scrolling_about_page_can_reach_both_vertical_bounds_after_resize()
    {
        RunOnSta(() =>
        {
            EnsureApplication();

            var installRoot = CreateInstallRoot();
            try
            {
                var controller = CreateController(installRoot);
                controller.Should().NotBeNull();

                var window = GetWindow(controller);
                try
                {
                    window.Width = 760;
                    window.Height = 520;
                    WpfStaTestHost.ShowAndWait(window);
                    controller!.NavigateTo("about");
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    window.Width = 920;
                    window.Height = 680;
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    window.Width = 760;
                    window.Height = 520;
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    var contentScrollViewer = window.FindName("contentScrollViewer") as ScrollViewer;
                    contentScrollViewer.Should().NotBeNull();
                    contentScrollViewer!.ScrollableHeight.Should().BeGreaterThan(0);

                    for (var i = 0; i < 160; i++)
                        contentScrollViewer.LineDown();

                    window.Dispatcher.Invoke(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    contentScrollViewer.VerticalOffset.Should().BeApproximately(
                        contentScrollViewer.ScrollableHeight,
                        1.0,
                        "mouse-wheel style downward scrolling should still reach the last row after resize churn");

                    for (var i = 0; i < 160; i++)
                        contentScrollViewer.LineUp();

                    window.Dispatcher.Invoke(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    contentScrollViewer.VerticalOffset.Should().BeApproximately(
                        0,
                        1.0,
                        "mouse-wheel style upward scrolling should still return to the very top");
                }
                finally
                {
                    window.Close();
                }
            }
            finally
            {
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void User_dragging_about_page_scrollbar_thumb_can_reach_both_vertical_bounds_after_resize()
    {
        RunOnSta(() =>
        {
            EnsureApplication();

            var installRoot = CreateInstallRoot();
            try
            {
                var controller = CreateController(installRoot);
                controller.Should().NotBeNull();

                var window = GetWindow(controller);
                try
                {
                    window.Width = 760;
                    window.Height = 520;
                    WpfStaTestHost.ShowAndWait(window);
                    controller!.NavigateTo("about");
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    window.Width = 920;
                    window.Height = 680;
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    window.Width = 760;
                    window.Height = 520;
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    var contentScrollViewer = window.FindName("contentScrollViewer") as ScrollViewer;
                    contentScrollViewer.Should().NotBeNull();
                    contentScrollViewer!.ScrollableHeight.Should().BeGreaterThan(0);

                    var verticalScrollBar = FindVerticalScrollBar(contentScrollViewer);
                    verticalScrollBar.Should().NotBeNull();

                    verticalScrollBar!.Maximum.Should().BeApproximately(
                        contentScrollViewer.ScrollableHeight,
                        1.0,
                        "the visual scrollbar range should stay synced with the real scrollable height after resize churn");

                    GenuineVerticalThumbDrag(window, verticalScrollBar, toBottom: true);
                    contentScrollViewer.VerticalOffset.Should().BeApproximately(
                        contentScrollViewer.ScrollableHeight,
                        1.0,
                        "dragging the thumb to the bottom should reach the true bottom");

                    GenuineVerticalThumbDrag(window, verticalScrollBar, toBottom: false);
                    contentScrollViewer.VerticalOffset.Should().BeApproximately(
                        0,
                        1.0,
                        "dragging the thumb back to the top should reach the true top");
                }
                finally
                {
                    window.Close();
                }
            }
            finally
            {
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void User_navigating_from_scrolled_advanced_to_about_starts_at_top()
    {
        RunOnSta(() =>
        {
            EnsureApplication();

            var installRoot = CreateInstallRoot();
            try
            {
                var controller = CreateController(installRoot);
                controller.Should().NotBeNull();

                var window = GetWindow(controller);
                try
                {
                    window.Width = 760;
                    window.Height = 520;
                    WpfStaTestHost.ShowAndWait(window);
                    controller!.NavigateTo("advanced");
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    var contentScrollViewer = window.FindName("contentScrollViewer") as ScrollViewer;
                    contentScrollViewer.Should().NotBeNull();
                    contentScrollViewer!.ScrollableHeight.Should().BeGreaterThan(0);

                    for (var i = 0; i < 120; i++)
                        contentScrollViewer.LineDown();

                    window.Dispatcher.Invoke(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    contentScrollViewer.VerticalOffset.Should().BeGreaterThan(0,
                        "advanced page should be scrolled away from the top before switching pages");

                    controller.NavigateTo("about");
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    contentScrollViewer.VerticalOffset.Should().BeApproximately(
                        0,
                        1.0,
                        "navigating to about should reset the shared content scroll viewer to the top");
                }
                finally
                {
                    window.Close();
                }
            }
            finally
            {
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void User_returning_from_logs_to_about_starts_at_top()
    {
        RunOnSta(() =>
        {
            EnsureApplication();

            var installRoot = CreateInstallRoot();
            File.WriteAllText(Path.Combine(installRoot, "SmartGuard.log"), "[INFO] 2026-07-04 06:00:00 ok\n");

            try
            {
                var controller = CreateController(installRoot);
                controller.Should().NotBeNull();

                var window = GetWindow(controller);
                try
                {
                    window.Width = 760;
                    window.Height = 520;
                    WpfStaTestHost.ShowAndWait(window);
                    controller!.NavigateTo("about");
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    var contentScrollViewer = window.FindName("contentScrollViewer") as ScrollViewer;
                    contentScrollViewer.Should().NotBeNull();
                    contentScrollViewer!.ScrollableHeight.Should().BeGreaterThan(0);

                    for (var i = 0; i < 120; i++)
                        contentScrollViewer.LineDown();

                    window.Dispatcher.Invoke(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    contentScrollViewer.VerticalOffset.Should().BeGreaterThan(0,
                        "about page should be scrolled away from the top before visiting logs");

                    controller.NavigateTo("logs");
                    window.UpdateLayout();
                    SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller, window);

                    controller.NavigateTo("about");
                    window.UpdateLayout();
                    var pageAbout = window.FindName("pageAbout") as FrameworkElement;
                    pageAbout.Should().NotBeNull();
                    WpfStaTestHost.WaitUntil(
                        () =>
                            pageAbout!.Visibility == Visibility.Visible &&
                            pageAbout.IsVisible,
                        TimeSpan.FromSeconds(10),
                        "the About page to become visible after returning from logs");
                    WpfStaTestHost.WaitForRender();
                    WpfStaTestHost.WaitUntil(
                        () => Math.Abs(contentScrollViewer.VerticalOffset) <= 1.0,
                        TimeSpan.FromSeconds(10),
                        "the About page vertical offset to return to the top");

                    contentScrollViewer.VerticalOffset.Should().BeApproximately(
                        0,
                        1.0,
                        "returning from logs to about should reset the shared content scroll viewer to the top");
                }
                finally
                {
                    window.Close();
                }
            }
            finally
            {
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void User_maximizes_window_reapplies_dark_title_bar_request()
    {
        RunOnSta(() =>
        {
            EnsureApplication();

            var installRoot = CreateInstallRoot();
            try
            {
                var controller = CreateController(installRoot);
                controller.Should().NotBeNull();

                var window = GetWindow(controller);
                var tglThemeDark = window.FindName("tglThemeDark") as CheckBox;
                tglThemeDark.Should().NotBeNull();
                tglThemeDark!.IsChecked = true;

                SettingsWindowLayoutStability.HandleWindowStateChanged(window, isDarkTheme: true, WindowState.Maximized);
                WindowTitleBarTheme.GetLastRequestedDarkMode(window).Should().BeTrue();
            }
            finally
            {
                TryDelete(installRoot);
            }
        });
    }

    private static string RepoXamlPath() => SettingsSourcePaths.ProjectSettingsXamlPath();

    private static string RepoLayoutStabilityPath()
        => Path.Combine(SettingsSourcePaths.RepoRoot(), "src", "SmartGuard.Settings", "SettingsWindowLayoutStability.cs");

    private static void GenuineVerticalThumbDrag(Window window, ScrollBar scrollBar, bool toBottom)
    {
        scrollBar.ApplyTemplate();
        var track = scrollBar.Template?.FindName("PART_Track", scrollBar) as Track;
        track.Should().NotBeNull();
        var thumb = track!.Thumb;
        var delta = toBottom ? 200.0 : -200.0;

        thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        Pump(window);
        for (var i = 0; i < 20; i++)
        {
            thumb.RaiseEvent(new DragDeltaEventArgs(0, delta) { RoutedEvent = Thumb.DragDeltaEvent });
            Pump(window);
        }

        thumb.RaiseEvent(new DragCompletedEventArgs(0, delta * 20, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Pump(window);
    }

    private static void Pump(Window window)
        => window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

    private static ScrollBar? FindVerticalScrollBar(ScrollViewer scrollViewer)
    {
        scrollViewer.ApplyTemplate();
        return scrollViewer.Template?.FindName("PART_VerticalScrollBar", scrollViewer) as ScrollBar;
    }

    private static SettingsWindowController? CreateController(string installRoot)
    {
        var configPath = Path.Combine(installRoot, "SmartGuard.config.json");
        File.WriteAllText(
            configPath,
            "{\"BalancedThresholdSec\":300,\"PowerSaverThresholdSec\":900,\"LowBatteryPercent\":25,\"CheckIntervalSec\":30,\"BrightnessRestoreMs\":1000,\"ThemeFollowSystem\":false,\"ThemeIsDark\":false}");
        var repository = new GuardConfigRepository(configPath);
        var config = repository.LoadOrDefault(installRoot);
        return SettingsWindowController.TryCreate(installRoot, repository, config);
    }

    private static string CreateInstallRoot()
    {
        var installRoot = Path.Combine(Path.GetTempPath(), "sg-resize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(Path.Combine(installRoot, "bin"));
        return installRoot;
    }

    private static Window GetWindow(SettingsWindowController controller)
    {
        var field = typeof(SettingsWindowController).GetField(
            "_window",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (Window)field!.GetValue(controller)!;
    }

    private static void EnsureApplication() => WpfStaTestHost.EnsureApplication();

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, true); } catch { }
    }

    private static void RunOnSta(Action action) => WpfStaTestHost.Run(action);
}
