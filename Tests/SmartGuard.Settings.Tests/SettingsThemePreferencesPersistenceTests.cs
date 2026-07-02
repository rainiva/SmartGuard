using System.IO;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class SettingsThemePreferencesPersistenceTests
{
  [Fact]
  public void Toggling_manual_dark_theme_persists_ThemeIsDark_to_config_file()
  {
    WpfStaTestHost.Run(() =>
    {
      LogViewTagPalette.ConfigureForDarkMode(false);
      var installRoot = CreateInstallRoot(themeFollowSystem: false, themeIsDark: false);
      var configPath = Path.Combine(installRoot, "SmartGuard.config.json");
      try
      {
        var controller = CreateController(installRoot);
        controller!.IsDarkThemeEnabled.Should().BeFalse();

        var window = GetWindow(controller);
        var tglThemeDark = window.FindName("tglThemeDark") as CheckBox;
        tglThemeDark.Should().NotBeNull();
        tglThemeDark!.IsChecked = true;

        controller.IsDarkThemeEnabled.Should().BeTrue();

        var reloaded = new GuardConfigRepository(configPath).TryLoad();
        reloaded.Should().NotBeNull();
        reloaded!.ThemeIsDark.Should().BeTrue("theme toggle must persist immediately via SettingsSaveCoordinator");
      }
      finally
      {
        LogViewTagPalette.ConfigureForDarkMode(false);
        TryDelete(installRoot);
      }
    });
  }

  [Fact]
  public void Enabling_follow_system_persists_ThemeFollowSystem_to_config_file()
  {
    WpfStaTestHost.Run(() =>
    {
      LogViewTagPalette.ConfigureForDarkMode(false);
      var installRoot = CreateInstallRoot(themeFollowSystem: false, themeIsDark: false);
      var configPath = Path.Combine(installRoot, "SmartGuard.config.json");
      try
      {
        var controller = CreateController(installRoot);
        var window = GetWindow(controller);
        var tglThemeFollowSystem = window.FindName("tglThemeFollowSystem") as CheckBox;
        tglThemeFollowSystem.Should().NotBeNull();
        tglThemeFollowSystem!.IsChecked = true;

        var reloaded = new GuardConfigRepository(configPath).TryLoad();
        reloaded.Should().NotBeNull();
        reloaded!.ThemeFollowSystem.Should().BeTrue("follow-system toggle must persist immediately via SettingsSaveCoordinator");
      }
      finally
      {
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

  private static string CreateInstallRoot(bool themeFollowSystem, bool themeIsDark)
  {
    var installRoot = Path.Combine(Path.GetTempPath(), "sg-theme-persist-" + Guid.NewGuid().ToString("N"));
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
}
