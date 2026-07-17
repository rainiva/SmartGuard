using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class SettingsLogNavResponsivenessTests
{
    private const string LoadingStatusText = "正在加载日志…";

    [Fact]
    public void NavigateTo_logs_returns_before_slow_disk_read_completes()
    {
        WpfStaTestHost.Run(() =>
        {
            var installRoot = CreateInstallRoot();
            File.WriteAllText(
                Path.Combine(installRoot, "SmartGuard.log"),
                "[INFO] 2026-06-21 10:00:00 slow-disk line\n");

            SettingsWindowController? controller = null;
            try
            {
                LogTailReaderTestMetrics.Reset();
                LogTailReaderTestMetrics.ArtificialDelayMs = 500;

                controller = CreateController(installRoot);
                var window = GetWindow(controller!);
                WpfStaTestHost.ShowAndWait(window);

                var sw = Stopwatch.StartNew();
                controller!.NavigateTo("logs");
                sw.Stop();

                sw.Elapsed.Should().BeLessThan(
                    TimeSpan.FromMilliseconds(200),
                    "navigating to logs must not block the UI thread on disk I/O");

                var status = window.FindName("lblLogStatus") as TextBlock;
                status.Should().NotBeNull();
                status!.Text.Should().Be(LoadingStatusText);
            }
            finally
            {
                LogTailReaderTestMetrics.Reset();
                CloseControllerWindow(controller);
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void NavigateTo_logs_eventually_loads_log_content()
    {
        WpfStaTestHost.Run(() =>
        {
            var installRoot = CreateInstallRoot();
            File.WriteAllText(
                Path.Combine(installRoot, "SmartGuard.log"),
                "[INFO] 2026-06-21 10:00:00 first visit\n");

            SettingsWindowController? controller = null;
            try
            {
                LogTailReaderTestMetrics.Reset();
                controller = CreateController(installRoot);
                var window = GetWindow(controller!);
                WpfStaTestHost.ShowAndWait(window);

                controller!.NavigateTo("logs");
                WaitUntilLogContent(window);

                var lstLogView = window.FindName("lstLogView") as ListBox;
                lstLogView.Should().NotBeNull();
                lstLogView!.Items.Count.Should().BeGreaterThan(0);

                var status = window.FindName("lblLogStatus") as TextBlock;
                status!.Text.Should().NotBe(LoadingStatusText);
            }
            finally
            {
                CloseControllerWindow(controller);
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void Revisit_logs_keeps_list_and_does_not_force_full_redraw()
    {
        WpfStaTestHost.Run(() =>
        {
            var installRoot = CreateInstallRoot();
            File.WriteAllText(
                Path.Combine(installRoot, "SmartGuard.log"),
                "[INFO] 2026-06-21 10:00:00 keep me\n");

            SettingsWindowController? controller = null;
            try
            {
                controller = CreateController(installRoot);
                var window = GetWindow(controller!);
                WpfStaTestHost.ShowAndWait(window);
                controller!.NavigateTo("logs");
                WaitUntilLogContent(window);

                var lstLogView = window.FindName("lstLogView") as ListBox;
                var countBefore = lstLogView!.Items.Count;
                countBefore.Should().BeGreaterThan(0);

                SettingsWindowController.ResetTestMetricsForTests();
                controller.NavigateTo("general");
                DrainDispatcher(window);
                controller.NavigateTo("logs");
                DrainDispatcher(window);

                SettingsWindowController.ForceRefreshLogViewCountForTests.Should().Be(0,
                    "revisit with existing lines should use incremental refresh, not forceRedraw");
                lstLogView.Items.Count.Should().Be(countBefore);
            }
            finally
            {
                CloseControllerWindow(controller);
                TryDelete(installRoot);
            }
        });
    }

    internal static void WaitUntilLogContent(Window window, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            // Pump Background only — ApplicationIdle can hang forever while DispatcherTimer is active.
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

            var status = window.FindName("lblLogStatus") as TextBlock;
            if (status is not null && status.Text != LoadingStatusText)
                return;
        }

        throw new TimeoutException("Timed out waiting for log page content to load.");
    }

    internal static void WaitUntilLogRefreshIdle(SettingsWindowController controller, Window window, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

            if (!controller.IsLogRefreshInFlightForTests)
                return;
        }

        throw new TimeoutException("Timed out waiting for log refresh flight to finish.");
    }

    private static void CloseControllerWindow(SettingsWindowController? controller)
    {
        if (controller is null)
            return;

        try
        {
            GetWindow(controller).Close();
        }
        catch
        {
            // window may already be closed
        }
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
        var installRoot = Path.Combine(Path.GetTempPath(), "sg-log-nav-" + Guid.NewGuid().ToString("N"));
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

    private static void DrainDispatcher(Window window)
    {
        window.Dispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, true); } catch { }
    }
}
