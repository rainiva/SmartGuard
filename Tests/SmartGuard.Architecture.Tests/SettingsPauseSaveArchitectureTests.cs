using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsPauseSaveArchitectureTests
{
  [Fact]
  public void SettingsWindowController_must_route_pause_toggle_through_ConfigMutationService()
  {
    var policySource = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsPolicyCoordinator.cs");
    policySource.Should().Contain("ConfigMutationService");
    policySource.Should().Contain("SetPaused");
    policySource.Should().MatchRegex(@"_tglPaused\.(Checked|Unchecked)[\s\S]*OnPauseToggled",
      "pause toggle must not only call QueueSave");

    var controller = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsWindowController.cs");
    controller.Should().Contain("SettingsPolicyCoordinator");
    controller.Should().Contain("OnPauseToggled");
  }
}
