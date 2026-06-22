using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.LogViewer;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class SettingsColdStartTests
{
    [Fact]
    public void TryCreate_with_existing_log_does_not_read_log_file()
    {
        WpfStaTestHost.Run(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardColdStart_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var logPath = Path.Combine(tempRoot, "SmartGuard.log");
            File.WriteAllText(logPath, "[INFO] 2026-06-21 10:00:00 " + new string('x', 4000) + "\n");

            SettingsWindowController? controller = null;
            try
            {
                LogTailReaderTestMetrics.Reset();
                var repository = new GuardConfigRepository(Path.Combine(tempRoot, "SmartGuard.config.json"));
                var config = repository.LoadOrDefault(tempRoot);

                controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                LogTailReaderTestMetrics.ReadFromOffsetCallCount.Should().Be(0,
                    "cold start on the default page should defer log file reads");
            }
            finally
            {
                controller?.Dispose();
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void TryCreate_defers_log_view_controller_until_logs_page_is_opened()
    {
        WpfStaTestHost.Run(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardColdStart_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            File.WriteAllText(Path.Combine(tempRoot, "SmartGuard.log"), "[INFO] 2026-06-21 10:00:00 line\n");

            SettingsWindowController? controller = null;
            try
            {
                var repository = new GuardConfigRepository(Path.Combine(tempRoot, "SmartGuard.config.json"));
                var config = repository.LoadOrDefault(tempRoot);
                controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                controller!.IsLogViewInitializedForTests.Should().BeFalse();

                var window = GetWindow(controller);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                controller.IsLogViewInitializedForTests.Should().BeTrue();
            }
            finally
            {
                controller?.Dispose();
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void NavigateTo_logs_initializes_log_view_for_tray_shortcut()
    {
        WpfStaTestHost.Run(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardLogsShortcut_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            File.WriteAllText(Path.Combine(tempRoot, "SmartGuard.log"), "[INFO] 2026-06-21 10:00:00 tray shortcut\n");

            SettingsWindowController? controller = null;
            try
            {
                var repository = new GuardConfigRepository(Path.Combine(tempRoot, "SmartGuard.config.json"));
                var config = repository.LoadOrDefault(tempRoot);
                controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();
                controller!.IsLogViewInitializedForTests.Should().BeFalse();

                controller.NavigateTo("logs");
                var window = GetWindow(controller);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                controller.IsLogViewInitializedForTests.Should().BeTrue();
            }
            finally
            {
                controller?.Dispose();
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    private static Window GetWindow(SettingsWindowController controller)
    {
        var field = typeof(SettingsWindowController).GetField(
            "_window",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Window)field!.GetValue(controller)!;
    }
}
