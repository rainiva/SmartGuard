using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class SettingsLogPageRestoreTests
{
    [Fact]
    public void User_restores_log_page_from_taskbar_does_not_force_full_redraw()
    {
        WpfStaTestHost.Run(() =>
        {
            var installRoot = CreateInstallRoot();
            File.WriteAllText(
                Path.Combine(installRoot, "SmartGuard.log"),
                "[INFO] 2026-06-21 10:00:00 restored line\n");

            SettingsWindowController? controller = null;
            try
            {
                controller = CreateController(installRoot);
                controller.Should().NotBeNull();

                var window = GetWindow(controller!);
                WpfStaTestHost.ShowAndWait(window);
                controller!.NavigateTo("logs");
                DrainDispatcher(window);

                SettingsWindowController.ResetTestMetricsForTests();
                controller.SetLogPageActive(false);
                controller.SetLogPageActive(true, LogPageActivationReason.WindowRestored);
                DrainDispatcher(window);

                SettingsWindowController.ForceRefreshLogViewCountForTests.Should().Be(0,
                    "taskbar restore should not force a full log list redraw");
            }
            finally
            {
                CloseControllerWindow(controller);
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void User_restores_log_page_keeps_existing_list_content()
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
                DrainDispatcher(window);

                var lstLogView = window.FindName("lstLogView") as ListBox;
                lstLogView.Should().NotBeNull();
                var countBefore = lstLogView!.Items.Count;
                countBefore.Should().BeGreaterThan(0);

                controller.SetLogPageActive(false);
                controller.SetLogPageActive(true, LogPageActivationReason.WindowRestored);
                DrainDispatcher(window);

                lstLogView.Items.Count.Should().Be(countBefore);
            }
            finally
            {
                CloseControllerWindow(controller);
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void User_navigates_to_logs_still_loads_log_content()
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
                SettingsWindowController.ResetTestMetricsForTests();
                controller = CreateController(installRoot);
                var window = GetWindow(controller!);
                WpfStaTestHost.ShowAndWait(window);
                controller!.NavigateTo("logs");
                DrainDispatcher(window);

                SettingsWindowController.ForceRefreshLogViewCountForTests.Should().BeGreaterThan(0,
                    "first navigation to logs should load log content");

                var lstLogView = window.FindName("lstLogView") as ListBox;
                lstLogView.Should().NotBeNull();
                lstLogView!.Items.Count.Should().BeGreaterThan(0);
            }
            finally
            {
                CloseControllerWindow(controller);
                TryDelete(installRoot);
            }
        });
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
        var installRoot = Path.Combine(Path.GetTempPath(), "sg-log-restore-" + Guid.NewGuid().ToString("N"));
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
