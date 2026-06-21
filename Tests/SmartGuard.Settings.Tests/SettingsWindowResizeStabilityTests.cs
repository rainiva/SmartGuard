using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

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
                    window.Show();
                    controller!.NavigateTo("logs");
                    window.UpdateLayout();

                    window.Dispatcher.Invoke(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);

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

                    var logScrollViewer = window.FindName("logScrollViewer") as ScrollViewer;
                    logScrollViewer.Should().NotBeNull();
                    logScrollViewer!.ActualHeight.Should().BeGreaterThan(100);

                    SettingsWindowLayoutStability.StabilizeContentLayout(window);
                    SettingsWindowLayoutStability.HandleWindowStateChanged(window, isDarkTheme: false, WindowState.Maximized);
                    window.WindowState = WindowState.Maximized;
                    window.UpdateLayout();

                    window.Dispatcher.Invoke(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    logScrollViewer.ActualHeight.Should().BeGreaterThan(100);
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
                var btnThemeToggle = window.FindName("btnThemeToggle") as Button;
                btnThemeToggle.Should().NotBeNull();
                btnThemeToggle!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                SettingsWindowLayoutStability.HandleWindowStateChanged(window, isDarkTheme: true, WindowState.Maximized);
                WindowTitleBarTheme.LastRequestedDarkMode.Should().BeTrue();
            }
            finally
            {
                TryDelete(installRoot);
            }
        });
    }

    private static string RepoXamlPath()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var testProjectDir = Path.GetDirectoryName(assemblyLocation)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testProjectDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "lib", "SmartGuard.Settings.xaml");
    }

    private static SettingsWindowController? CreateController(string installRoot)
    {
        var configPath = Path.Combine(installRoot, "SmartGuard.config.json");
        File.WriteAllText(
            configPath,
            "{\"BalancedThresholdSec\":300,\"PowerSaverThresholdSec\":900,\"LowBatteryPercent\":25,\"CheckIntervalSec\":30,\"BrightnessRestoreMs\":1000}");
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

    private static void EnsureApplication()
    {
        if (Application.Current is not null)
            return;

        try { _ = new Application(); }
        catch (InvalidOperationException) { }
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, true); } catch { }
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw error;
    }
}
