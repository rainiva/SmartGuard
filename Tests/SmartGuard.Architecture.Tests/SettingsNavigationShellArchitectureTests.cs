using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsNavigationShellArchitectureTests
{
  [Fact]
  public void Navigation_behavior_lives_in_SettingsNavigationShell()
  {
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, "src/SmartGuard.Settings/SettingsNavigationShell.cs"))
      .Should().BeTrue();

    var shell = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsNavigationShell.cs");
    shell.Should().Contain("UpdatePageTitle");
    shell.Should().Contain("NavigateTo");

    var controller = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsWindowController.cs");
    controller.Should().Contain("SettingsNavigationShell");
    controller.Should().NotContain("private void SetupNavigation");
  }
}
