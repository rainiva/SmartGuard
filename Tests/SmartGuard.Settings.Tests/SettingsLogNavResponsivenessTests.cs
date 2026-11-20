using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class SettingsLogNavResponsivenessTests
{
    private const string LoadingStatusText = "正在加载日志…";

    [Fact]
    public void Logging_health_is_read_off_dispatcher_and_updates_without_new_log_lines()
    {
        WpfStaTestHost.Run(() =>
        {
            var root = CreateInstallRoot();
            SettingsWindowController? controller = null;
            var uiReads = 0;
            var allReads = 0;
            try
            {
                controller = CreateController(root);
                var window = GetWindow(controller!);
                LogViewIdleReader.ApiReadOverrideForTests = () =>
                {
                    Interlocked.Increment(ref allReads);
                    if (window.Dispatcher.CheckAccess()) Interlocked.Increment(ref uiReads);
                    return 0;
                };
                void WriteHealth(string code) => File.WriteAllText(SmartGuardPaths.StatusFile(root),
                    System.Text.Json.JsonSerializer.Serialize(new SmartGuard.Contracts.StatusPayload
                    {
                        timestamp = DateTimeOffset.UtcNow.ToString("O"),
                        logHealth = new SmartGuard.Contracts.LogHealthStatus(code, DateTimeOffset.UtcNow, 1, 0, 0, 0, 0, 0),
                    }), new System.Text.UTF8Encoding(false));
                WriteHealth("failed");
                WpfStaTestHost.ShowAndWait(window);
                controller!.NavigateTo("logs");
                WaitUntilLogContent(controller, window);
                WaitUntilLogRefreshIdle(controller, window);
                allReads.Should().BeGreaterThan(0);
                uiReads.Should().Be(0, "状态读取必须属于既有后台刷新任务");
                var status = (TextBlock)window.FindName("lblLogStatus");
                status.Text.Should().Contain("日志可能缺失");
                WriteHealth("healthy");
                var host = typeof(SettingsWindowController).GetField("_logPageHost", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
                host.GetType().GetMethod("DetectPagedLogChanges", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(host, null);
                WpfStaTestHost.WaitUntil(() => !status.Text.Contains("日志可能缺失"), TimeSpan.FromSeconds(5),
                    "健康变化在无新日志时经既有刷新节拍可见");
                uiReads.Should().Be(0);
            }
            finally
            {
                LogViewIdleReader.ApiReadOverrideForTests = null;
                CloseControllerWindow(controller);
                TryDelete(root);
            }
        });
    }

    [Fact]
    public void NavigateTo_logs_with_large_fixture_returns_before_disk_read_completes()
    {
        WpfStaTestHost.Run(() =>
        {
            using var fixture = LogWorkbenchPerformanceFixture.Create(
                archiveCount: 30,
                eventsPerArchive: 2000,
                currentLogTargetBytes: 10 * 1024 * 1024);

            SettingsWindowController? controller = null;
            try
            {
                LogTailReaderTestMetrics.Reset();
                LogTailReaderTestMetrics.ArtificialDelayMs = 500;

                controller = CreateControllerForLogPath(fixture.LogPath);
                WpfStaTestHost.Track(controller!);
                var window = GetWindow(controller!);
                WpfStaTestHost.ShowAndWait(window);

                var sw = Stopwatch.StartNew();
                controller!.NavigateTo("logs");
                sw.Stop();

                sw.Elapsed.Should().BeLessThan(
                    TimeSpan.FromMilliseconds(200),
                    "navigating to logs with a 10MB + 30 archive fixture must not block the UI thread on disk I/O");

                var status = window.FindName("lblLogStatus") as TextBlock;
                status.Should().NotBeNull();
                status!.Text.Should().Be(LoadingStatusText);
            }
            finally
            {
                LogTailReaderTestMetrics.Reset();
                CloseControllerWindow(controller);
            }
        });
    }

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
                WpfStaTestHost.Track(controller!);
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
                WaitUntilLogContent(controller, window);

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
                WaitUntilLogContent(controller, window);

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

    internal static void WaitUntilLogContent(
        SettingsWindowController controller,
        Window window,
        int timeoutMs = 10000)
    {
        WpfStaTestHost.Track(controller);
        var status = window.FindName("lblLogStatus") as TextBlock;
        var list = window.FindName("lstLogView") as ListBox;
        var table = window.FindName("logTable") as DataGrid;
        var total = window.FindName("lblLogTotalCount") as TextBlock;
        try
        {
            WpfStaTestHost.WaitUntil(
                () =>
                    status is not null &&
                    status.Text != LoadingStatusText &&
                    !controller.IsLogRefreshInFlightForTests &&
                    ((list?.Items.Count > 0) ||
                     (table?.Items.Count > 0) ||
                     (total?.Text.StartsWith("共 ", StringComparison.Ordinal) == true)),
                TimeSpan.FromMilliseconds(timeoutMs),
                "log items and status to settle");
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"{ex.Message} Log items={list?.Items.Count ?? -1}, " +
                $"table items={table?.Items.Count ?? -1}, " +
                $"total='{total?.Text ?? "<missing>"}', " +
                $"status='{status?.Text ?? "<missing>"}', " +
                $"refresh in flight={controller.IsLogRefreshInFlightForTests}, " +
                $"active refresh={controller.ActiveLogRefreshCountForTests}.",
                ex);
        }
    }

    internal static void WaitUntilLogRefreshIdle(SettingsWindowController controller, Window window, int timeoutMs = 10000)
    {
        WpfStaTestHost.Track(controller);
        WpfStaTestHost.WaitUntil(
            () => !controller.IsLogRefreshInFlightForTests,
            TimeSpan.FromMilliseconds(timeoutMs),
            "the active log refresh flight to finish");
    }

    internal static void PumpDispatcher(Window window)
    {
        if (!window.Dispatcher.CheckAccess())
        {
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            return;
        }

        var frame = new DispatcherFrame();
        _ = window.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
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
        => CreateControllerForLogPath(Path.Combine(installRoot, "SmartGuard.log"), installRoot);

    private static SettingsWindowController? CreateControllerForLogPath(string logPath, string? installRoot = null)
    {
        installRoot ??= Path.GetDirectoryName(logPath)!;
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(Path.Combine(installRoot, "bin"));
        if (!File.Exists(logPath))
            File.WriteAllText(logPath, "[INFO] 2026-06-21 10:00:00 placeholder\n");

        var configPath = Path.Combine(installRoot, "SmartGuard.config.json");
        File.WriteAllText(
            configPath,
            "{\"BalancedThresholdSec\":300,\"PowerSaverThresholdSec\":900,\"LowBatteryPercent\":25,\"CheckIntervalSec\":30,\"BrightnessRestoreMs\":1000,\"ThemeFollowSystem\":false,\"ThemeIsDark\":false,\"LogFile\":\"" +
            logPath.Replace("\\", "\\\\") +
            "\"}");
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
        PumpDispatcher(window);
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, true); } catch { }
    }
}
