using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsAboutCoordinatorArchitectureTests
{
  [Fact]
  public void About_page_behavior_lives_in_SettingsAboutCoordinator()
  {
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, "src/SmartGuard.Settings/SettingsAboutCoordinator.cs"))
      .Should().BeTrue();

    var coordinator = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsAboutCoordinator.cs");
    coordinator.Should().Contain("GetDisplayVersion");
    coordinator.Should().Contain("SettingsUpdateCheckCoordinator");

    var updateCheck = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsUpdateCheckCoordinator.cs");
    updateCheck.Should().Contain("CheckForUpdateAsync");

    var controller = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsWindowController.cs");
    controller.Should().Contain("SettingsAboutCoordinator");
    controller.Should().NotContain("CreateDownloadProgressWindow");
  }
}
