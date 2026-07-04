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
    coordinator.Should().Contain("SettingsDebouncedSaver");
    coordinator.Should().NotContain("private async void SaveCurrentSettings");

    var controller = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsWindowController.cs");
    controller.Should().Contain("SettingsPolicyCoordinator");
    controller.Should().NotContain("private async void SaveCurrentSettings");
  }

  [Fact]
  public void Pause_toggle_must_live_in_SettingsPauseHandler()
  {
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, "src/SmartGuard.Settings/SettingsPauseHandler.cs"))
      .Should().BeTrue();

    var coordinator = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsPolicyCoordinator.cs");
    coordinator.Should().Contain("SettingsPauseHandler");
    coordinator.Should().NotContain("ConfigMutationService");
    coordinator.Should().NotContain("SetPaused");

    var pauseHandler = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsPauseHandler.cs");
    pauseHandler.Should().Contain("ConfigMutationService");
    pauseHandler.Should().Contain("SetPaused");
  }

  [Fact]
  public void Debounced_save_must_live_in_SettingsDebouncedSaver()
  {
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, "src/SmartGuard.Settings/SettingsDebouncedSaver.cs"))
      .Should().BeTrue();

    var saver = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsDebouncedSaver.cs");
    saver.Should().Contain("SettingsSaveCoordinator");
    saver.Should().Contain("QueueSave");
  }
}
