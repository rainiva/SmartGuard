using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public sealed class SettingsLogPageHostLifetimeTests
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public void Dispose_during_blocked_refresh_releases_background_lifetime_without_applying_to_ui()
    {
        WpfStaTestHost.Run(() =>
        {
            var installRoot = CreateInstallRoot();
            using var readEntered = new ManualResetEventSlim();
            using var releaseRead = new ManualResetEventSlim();
            SettingsWindowController? controller = null;

            try
            {
                WriteLog(installRoot);
                LogTailReaderTestMetrics.Reset();
                LogTailReaderTestMetrics.BeforeReadForTests = () =>
                {
                    readEntered.Set();
                    if (!releaseRead.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("Test did not release the blocked log read.");
                };

                controller = CreateController(installRoot);
                var window = GetWindow(controller);
                WpfStaTestHost.ShowAndWait(window);

                controller.NavigateTo("logs");
                WpfStaTestHost.WaitUntil(
                    () => readEntered.IsSet,
                    window: null,
                    "the background log read to start",
                    pumpDispatcher: false);

                var disposeDuration = Stopwatch.StartNew();
                controller.Dispose();
                disposeDuration.Stop();

                disposeDuration.Elapsed.Should().BeLessThan(
                    TimeSpan.FromMilliseconds(200),
                    "Dispose must cancel ownership without synchronously waiting for disk I/O");

                releaseRead.Set();
                WpfStaTestHost.WaitUntil(
                    () => !controller.IsLogRefreshInFlightForTests,
                    window: null,
                    "the background log refresh lifetime to finish",
                    pumpDispatcher: false);

                controller.ActiveLogRefreshCountForTests.Should().Be(0);
                WpfStaTestHost.PumpDispatcher(window);
                controller.ApplyAfterDisposeCountForTests.Should().Be(0);
            }
            finally
            {
                releaseRead.Set();
                controller?.Dispose();
                LogTailReaderTestMetrics.Reset();
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void Read_failure_exits_loading_state_and_shows_failure_status()
    {
        WpfStaTestHost.Run(() =>
        {
            var installRoot = CreateInstallRoot();
            SettingsWindowController? controller = null;

            try
            {
                WriteLog(installRoot);
                LogTailReaderTestMetrics.Reset();
                var readAttempt = 0;
                LogTailReaderTestMetrics.BeforeReadForTests = () =>
                {
                    if (Interlocked.Increment(ref readAttempt) == 1)
                        throw new IOException("simulated log read failure");
                };

                controller = CreateController(installRoot);
                var window = GetWindow(controller);
                WpfStaTestHost.ShowAndWait(window);

                controller.NavigateTo("logs");
                var status = window.FindName("lblLogStatus") as TextBlock;
                status.Should().NotBeNull();

                WpfStaTestHost.WaitUntil(
                    () => status!.Text.Contains("加载失败", StringComparison.Ordinal),
                    window,
                    "the log page to show its failed loading state");

                status!.Text.Should().Contain("加载失败");
                controller.IsLogRefreshInFlightForTests.Should().BeFalse();
            }
            finally
            {
                controller?.Dispose();
                LogTailReaderTestMetrics.Reset();
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void Dispose_can_be_called_twice()
    {
        WpfStaTestHost.Run(() =>
        {
            var installRoot = CreateInstallRoot();
            SettingsWindowController? controller = null;

            try
            {
                controller = CreateController(installRoot);
                controller.Dispose();

                var secondDispose = () => controller.Dispose();

                secondDispose.Should().NotThrow();
            }
            finally
            {
                controller?.Dispose();
                LogTailReaderTestMetrics.Reset();
                TryDelete(installRoot);
            }
        });
    }

    private static SettingsWindowController CreateController(string installRoot)
    {
        var configPath = Path.Combine(installRoot, "SmartGuard.config.json");
        File.WriteAllText(
            configPath,
            "{\"BalancedThresholdSec\":300,\"PowerSaverThresholdSec\":900,\"LowBatteryPercent\":25,\"CheckIntervalSec\":30,\"BrightnessRestoreMs\":1000,\"ThemeFollowSystem\":false,\"ThemeIsDark\":false}",
            Utf8NoBom);
        var repository = new GuardConfigRepository(configPath);
        var config = repository.LoadOrDefault(installRoot);
        return SettingsWindowController.TryCreate(installRoot, repository, config)
            ?? throw new InvalidOperationException("Failed to create SettingsWindowController for WPF lifetime test.");
    }

    private static string CreateInstallRoot()
    {
        var installRoot = Path.Combine(Path.GetTempPath(), "sg-log-lifetime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(Path.Combine(installRoot, "bin"));
        return installRoot;
    }

    private static void WriteLog(string installRoot)
    {
        File.WriteAllText(
            Path.Combine(installRoot, "SmartGuard.log"),
            "[INFO] 2026-07-26 10:00:00 lifetime test line\n",
            Utf8NoBom);
    }

    private static Window GetWindow(SettingsWindowController controller)
    {
        var field = typeof(SettingsWindowController).GetField(
            "_window",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (Window)field!.GetValue(controller)!;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
