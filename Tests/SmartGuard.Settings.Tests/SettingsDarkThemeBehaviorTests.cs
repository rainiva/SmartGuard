using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class SettingsDarkThemeBehaviorTests
{
    [Fact]
    public void User_enables_manual_dark_mode_log_body_color_becomes_readable_on_dark_background()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            LogViewTagPalette.ConfigureForDarkMode(false);

            var installRoot = CreateInstallRoot(themeFollowSystem: false);
            try
            {
                var controller = CreateController(installRoot);
                controller.Should().NotBeNull();

                var window = GetWindow(controller);
                SetManualDarkMode(window, enabled: true);

                LogViewTagPalette.GetBodyColor().Should().Be(Color.FromRgb(0xE8, 0xE8, 0xE8));
            }
            finally
            {
                LogViewTagPalette.ConfigureForDarkMode(false);
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void User_enables_manual_dark_mode_requests_dark_title_bar()
    {
        RunOnSta(() =>
        {
            EnsureApplication();

            var installRoot = CreateInstallRoot(themeFollowSystem: false);
            try
            {
                var controller = CreateController(installRoot);
                controller.Should().NotBeNull();

                var window = GetWindow(controller);
                WindowTitleBarTheme.Apply(window, isDarkMode: false);
                try
                {
                    WpfStaTestHost.ShowAndWait(window);
                    WindowTitleBarTheme.GetLastRequestedDarkMode(window).Should().BeFalse();

                    SetManualDarkMode(window, enabled: true);

                    WindowTitleBarTheme.GetLastRequestedDarkMode(window).Should().BeTrue();
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

    private static SettingsWindowController? CreateController(string installRoot)
    {
        var configPath = Path.Combine(installRoot, "SmartGuard.config.json");
        var repository = new GuardConfigRepository(configPath);
        var config = repository.LoadOrDefault(installRoot);
        config.ThemeFollowSystem = false;
        config.ThemeIsDark = false;
        return SettingsWindowController.TryCreate(installRoot, repository, config);
    }

    private static string CreateInstallRoot(bool themeFollowSystem)
    {
        var installRoot = Path.Combine(Path.GetTempPath(), "sg-dark-theme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(Path.Combine(installRoot, "bin"));
        File.WriteAllText(
            Path.Combine(installRoot, "SmartGuard.config.json"),
            $$"""
            {
              "BalancedThresholdSec":300,
              "PowerSaverThresholdSec":900,
              "LowBatteryPercent":25,
              "CheckIntervalSec":30,
              "BrightnessRestoreMs":1000,
              "ThemeFollowSystem":{{themeFollowSystem.ToString().ToLowerInvariant()}},
              "ThemeIsDark":false
            }
            """);
        return installRoot;
    }

    private static void SetManualDarkMode(Window window, bool enabled)
    {
        var tglThemeDark = window.FindName("tglThemeDark") as CheckBox;
        tglThemeDark.Should().NotBeNull();
        tglThemeDark!.IsChecked = enabled;
    }

    private static Window GetWindow(SettingsWindowController? controller)
    {
        var field = typeof(SettingsWindowController).GetField(
            "_window",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Window)field!.GetValue(controller!)!;
    }

    private static void EnsureApplication() => WpfStaTestHost.EnsureApplication();

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, true); } catch { }
    }

    private static void RunOnSta(Action action)
    {
        WpfStaTestHost.Run(action);
    }
}
