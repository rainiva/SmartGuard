using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsThemeCoordinatorArchitectureTests
{
  [Fact]
  public void Theme_behavior_lives_in_SettingsThemeCoordinator()
  {
    var path = Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Settings",
      "SettingsThemeCoordinator.cs");
    File.Exists(path).Should().BeTrue();

    var coordinator = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsThemeCoordinator.cs");
    coordinator.Should().Contain("SettingsThemeState");
    coordinator.Should().Contain("SaveThemePreferences");

    var controller = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsWindowController.cs");
    controller.Should().Contain("SettingsThemeCoordinator");
    controller.Should().NotContain("private void SaveThemePreferences");
  }
}
