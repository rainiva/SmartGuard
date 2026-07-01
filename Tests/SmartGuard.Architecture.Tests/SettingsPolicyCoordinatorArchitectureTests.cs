using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsPolicyCoordinatorArchitectureTests
{
  [Fact]
  public void Policy_save_behavior_lives_in_SettingsPolicyCoordinator()
  {
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, "src/SmartGuard.Settings/SettingsPolicyCoordinator.cs"))
      .Should().BeTrue();

    var coordinator = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsPolicyCoordinator.cs");
    coordinator.Should().Contain("ConfigMutationService");
    coordinator.Should().Contain("SetPaused");
    coordinator.Should().Contain("SaveCurrentSettings");

    var controller = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsWindowController.cs");
    controller.Should().Contain("SettingsPolicyCoordinator");
    controller.Should().NotContain("private async void SaveCurrentSettings");
  }
}
