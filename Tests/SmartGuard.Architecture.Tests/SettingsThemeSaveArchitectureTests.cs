using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsThemeSaveArchitectureTests
{
  [Fact]
  public void SettingsPolicyCoordinator_SaveThemePreferences_must_call_SettingsSaveCoordinator()
  {
    var policy = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsPolicyCoordinator.cs");
    policy.Should().Contain("SaveThemePreferences");
    policy.Should().Contain("SettingsSaveCoordinator.Save");
  }

  [Fact]
  public void SettingsWindowController_must_wire_theme_save_through_policy_coordinator()
  {
    var controller = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsWindowController.cs");
    controller.Should().Contain("policyCoordinator.SaveThemePreferences");
    controller.Should().NotContain("policyCoordinator.CommitSavedConfig,");
  }
}
