using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsPauseSaveArchitectureTests
{
  [Fact]
  public void SettingsWindowController_must_route_pause_toggle_through_ConfigMutationService()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsWindowController.cs");
    source.Should().Contain("ConfigMutationService");
    source.Should().Contain("SetPaused");
    source.Should().MatchRegex(@"tglPaused\.(Checked|Unchecked)[\s\S]*OnPauseToggled",
      "pause toggle must not only call QueueSave");
  }
}
