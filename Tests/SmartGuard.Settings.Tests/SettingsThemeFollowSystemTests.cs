using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartGuard.Configuration;
using SmartGuard.Settings;
using Thickness = System.Windows.Thickness;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class SettingsThemeFollowSystemTests
{
    [Fact]
    public void Theme_follow_system_on_hides_manual_theme_rows_and_follow_system_divider()
    {
        RunOnSta(() =>
        {
            var installRoot = CreateInstallRoot(themeFollowSystem: true);
            try
            {
                var controller = CreateController(installRoot);
                var window = GetWindow(controller!);
                var rowThemeFollowSystem = window.FindName("rowThemeFollowSystem") as Border;
                var rowThemeLight = window.FindName("rowThemeLight") as Border;
                var rowThemeDark = window.FindName("rowThemeDark") as Border;

                rowThemeFollowSystem.Should().NotBeNull();
                rowThemeFollowSystem!.BorderThickness.Should().Be(new Thickness(0));
                rowThemeLight.Should().NotBeNull();
                rowThemeLight!.Visibility.Should().Be(Visibility.Collapsed);
                rowThemeDark.Should().NotBeNull();
                rowThemeDark!.Visibility.Should().Be(Visibility.Collapsed);
            }
            finally
            {
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void Theme_follow_system_off_shows_manual_theme_rows_and_follow_system_divider()
    {
        RunOnSta(() =>
        {
            var installRoot = CreateInstallRoot(themeFollowSystem: false);
            try
            {
                var controller = CreateController(installRoot);
                var window = GetWindow(controller!);
                var rowThemeFollowSystem = window.FindName("rowThemeFollowSystem") as Border;
                var rowThemeLight = window.FindName("rowThemeLight") as Border;
                var rowThemeDark = window.FindName("rowThemeDark") as Border;

                rowThemeFollowSystem.Should().NotBeNull();
                rowThemeFollowSystem!.BorderThickness.Should().Be(new Thickness(0, 0, 0, 1));
                rowThemeLight.Should().NotBeNull();
                rowThemeLight!.Visibility.Should().Be(Visibility.Visible);
                rowThemeDark.Should().NotBeNull();
                rowThemeDark!.Visibility.Should().Be(Visibility.Visible);
            }
            finally
            {
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void User_toggles_manual_dark_mode_applies_theme()
    {
        RunOnSta(() =>
        {
            LogViewTagPalette.ConfigureForDarkMode(false);
            var installRoot = CreateInstallRoot(themeFollowSystem: false, themeIsDark: false);
            try
            {
                var controller = CreateController(installRoot);
                controller!.IsDarkThemeEnabled.Should().BeFalse();

                var window = GetWindow(controller);
                var tglThemeLight = window.FindName("tglThemeLight") as CheckBox;
                var tglThemeDark = window.FindName("tglThemeDark") as CheckBox;
                tglThemeLight.Should().NotBeNull();
                tglThemeDark.Should().NotBeNull();
                tglThemeDark!.IsChecked = true;

                controller.IsDarkThemeEnabled.Should().BeTrue();
                tglThemeLight!.IsChecked.Should().BeFalse();
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
    public void User_toggles_manual_light_mode_applies_theme()
    {
        RunOnSta(() =>
        {
            LogViewTagPalette.ConfigureForDarkMode(false);
            var installRoot = CreateInstallRoot(themeFollowSystem: false, themeIsDark: true);
            try
            {
                var controller = CreateController(installRoot);
                controller!.IsDarkThemeEnabled.Should().BeTrue();

                var window = GetWindow(controller);
                var tglThemeLight = window.FindName("tglThemeLight") as CheckBox;
                var tglThemeDark = window.FindName("tglThemeDark") as CheckBox;
                tglThemeLight.Should().NotBeNull();
                tglThemeDark.Should().NotBeNull();
                tglThemeLight!.IsChecked = true;

                controller.IsDarkThemeEnabled.Should().BeFalse();
                tglThemeDark!.IsChecked.Should().BeFalse();
            }
            finally
            {
                LogViewTagPalette.ConfigureForDarkMode(false);
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void User_disables_follow_system_after_window_shown_does_not_throw()
    {
        RunOnSta(() =>
        {
            SettingsUiTestMode.SetEnabled(false);
            SystemThemeWatcher.RegistryReaderForTests = () => 1;
            var installRoot = CreateInstallRoot(themeFollowSystem: true, themeIsDark: false);
            try
            {
                var controller = CreateController(installRoot);
                controller.Should().NotBeNull();

                var window = GetWindow(controller!);
                WpfStaTestHost.ShowAndWait(window);

                var tglThemeFollowSystem = window.FindName("tglThemeFollowSystem") as CheckBox;
                tglThemeFollowSystem.Should().NotBeNull();

                var act = () => tglThemeFollowSystem!.IsChecked = false;
                act.Should().NotThrow("disabling follow-system must not crash theme transition animation");

                controller!.IsDarkThemeEnabled.Should().BeFalse();
            }
            finally
            {
                SettingsUiTestMode.SetEnabled(true);
                SystemThemeWatcher.ResetForTests();
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void User_disables_follow_system_while_system_is_dark_applies_manual_light_without_throw()
    {
        RunOnSta(() =>
        {
            SettingsUiTestMode.SetEnabled(false);
            SystemThemeWatcher.RegistryReaderForTests = () => 0;
            var installRoot = CreateInstallRoot(themeFollowSystem: true, themeIsDark: false);
            try
            {
                var controller = CreateController(installRoot);
                controller.Should().NotBeNull();
                controller!.IsDarkThemeEnabled.Should().BeTrue();

                var window = GetWindow(controller);
                WpfStaTestHost.ShowAndWait(window);

                var tglThemeFollowSystem = window.FindName("tglThemeFollowSystem") as CheckBox;
                tglThemeFollowSystem.Should().NotBeNull();

                var act = () => tglThemeFollowSystem!.IsChecked = false;
                act.Should().NotThrow("switching from system-dark to manual-light must animate safely");

                controller.IsDarkThemeEnabled.Should().BeFalse();
            }
            finally
            {
                SettingsUiTestMode.SetEnabled(true);
                SystemThemeWatcher.ResetForTests();
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void User_enables_follow_system_applies_registry_dark_mode()
    {
        RunOnSta(() =>
        {
            LogViewTagPalette.ConfigureForDarkMode(false);
            SystemThemeWatcher.RegistryReaderForTests = () => 0;
            var installRoot = CreateInstallRoot(themeFollowSystem: false, themeIsDark: false);
            try
            {
                var controller = CreateController(installRoot);
                var window = GetWindow(controller!);
                var tglThemeFollowSystem = window.FindName("tglThemeFollowSystem") as CheckBox;
                tglThemeFollowSystem.Should().NotBeNull();
                tglThemeFollowSystem!.IsChecked = true;

                controller!.IsDarkThemeEnabled.Should().BeTrue();
            }
            finally
            {
                SystemThemeWatcher.ResetForTests();
                LogViewTagPalette.ConfigureForDarkMode(false);
                TryDelete(installRoot);
            }
        });
    }

    private static SettingsWindowController? CreateController(string installRoot)
    {
        var configPath = Path.Combine(installRoot, "SmartGuard.config.json");
        var repository = new GuardConfigRepository(configPath);
        var config = repository.LoadOrDefault(installRoot);
        return SettingsWindowController.TryCreate(installRoot, repository, config);
    }

    private static string CreateInstallRoot(bool themeFollowSystem = true, bool themeIsDark = false)
    {
        var installRoot = Path.Combine(Path.GetTempPath(), "sg-theme-follow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(Path.Combine(installRoot, "bin"));
        File.WriteAllText(
            Path.Combine(installRoot, "SmartGuard.config.json"),
            $$"""
            {
              "BalancedThresholdSec": 300,
              "PowerSaverThresholdSec": 900,
              "LowBatteryPercent": 25,
              "CheckIntervalSec": 30,
              "BrightnessRestoreMs": 1000,
              "ThemeFollowSystem": {{themeFollowSystem.ToString().ToLowerInvariant()}},
              "ThemeIsDark": {{themeIsDark.ToString().ToLowerInvariant()}}
            }
            """);
        return installRoot;
    }

    private static Window GetWindow(SettingsWindowController controller)
    {
        var field = typeof(SettingsWindowController).GetField(
            "_window",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Window)field!.GetValue(controller)!;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, true); } catch { }
    }

    private static void RunOnSta(Action action) => WpfStaTestHost.Run(action);
}
