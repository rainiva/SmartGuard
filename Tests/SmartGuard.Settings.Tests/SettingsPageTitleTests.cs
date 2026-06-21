using System.IO;
using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class SettingsPageTitleTests
{
    [Fact]
    public void User_navigates_settings_pages_updates_shared_page_title()
    {
        RunOnSta(() =>
        {
            EnsureApplication();

            var installRoot = CreateInstallRoot();
            try
            {
                var controller = SettingsWindowController.TryCreate(
                    installRoot,
                    new GuardConfigRepository(Path.Combine(installRoot, "SmartGuard.config.json")),
                    new GuardConfigRepository(Path.Combine(installRoot, "SmartGuard.config.json")).LoadOrDefault(installRoot));
                controller.Should().NotBeNull();

                var window = GetWindow(controller!);
                var navList = window.FindName("navList") as ListBox;
                var txtPageTitle = window.FindName("txtPageTitle") as TextBlock;
                navList.Should().NotBeNull();
                txtPageTitle.Should().NotBeNull();

                navList!.SelectedIndex = 2;
                txtPageTitle!.Text.Should().Be("通知设置");

                navList.SelectedIndex = 0;
                txtPageTitle.Text.Should().Be("常规设置");
            }
            finally
            {
                TryDelete(installRoot);
            }
        });
    }

    private static string CreateInstallRoot()
    {
        var installRoot = Path.Combine(Path.GetTempPath(), "sg-title-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(Path.Combine(installRoot, "bin"));
        File.WriteAllText(
            Path.Combine(installRoot, "SmartGuard.config.json"),
            "{\"BalancedThresholdSec\":300,\"PowerSaverThresholdSec\":900,\"LowBatteryPercent\":25,\"CheckIntervalSec\":30,\"BrightnessRestoreMs\":1000}");
        return installRoot;
    }

    private static Window GetWindow(SettingsWindowController controller)
    {
        var field = typeof(SettingsWindowController).GetField(
            "_window",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
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
