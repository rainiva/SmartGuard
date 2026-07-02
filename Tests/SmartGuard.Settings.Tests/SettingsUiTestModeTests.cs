using System.Windows;
using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class SettingsUiTestModeTests
{
    [Fact]
    public void CheckForUpdateAsync_skips_network_and_modal_dialogs_in_ui_test_mode()
    {
        SettingsUiTestMode.IsEnabled.Should().BeTrue();

        WpfStaTestHost.Run(() =>
        {
            var installRoot = Path.Combine(Path.GetTempPath(), "sg-test-update-mode-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(installRoot);
            Directory.CreateDirectory(Path.Combine(installRoot, "bin"));

            try
            {
                var configPath = Path.Combine(installRoot, "SmartGuard.config.json");
                File.WriteAllText(configPath, "{\"BalancedThresholdSec\":300,\"PowerSaverThresholdSec\":900,\"LowBatteryPercent\":25,\"CheckIntervalSec\":30,\"BrightnessRestoreMs\":1000}");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(installRoot);
                var controller = SettingsWindowController.TryCreate(installRoot, repository, config);
                controller.Should().NotBeNull();

                var window = typeof(SettingsWindowController)
                    .GetField("_window", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .GetValue(controller) as Window;
                window.Should().NotBeNull();

                var updateCheck = new SettingsUpdateCheckCoordinator();
                updateCheck.CheckForUpdateAsync(window!, () => null).GetAwaiter().GetResult();

                Application.Current!.Windows.OfType<Window>().Should().ContainSingle();
            }
            finally
            {
                try { Directory.Delete(installRoot, true); } catch { }
            }
        });
    }
}
