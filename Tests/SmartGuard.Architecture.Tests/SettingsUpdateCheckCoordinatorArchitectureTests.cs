using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsUpdateCheckCoordinatorArchitectureTests
{
  [Fact]
  public void Download_progress_window_must_live_in_UpdateDownloadProgressWindowFactory()
  {
    File.Exists(Path.Combine(
        SourceScanHelper.RepoRoot,
        "src",
        "SmartGuard.Settings",
        "UpdateDownloadProgressWindowFactory.cs"))
      .Should().BeTrue();

    var coordinator = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsUpdateCheckCoordinator.cs");
    coordinator.Should().Contain("UpdateDownloadProgressWindowFactory");
    coordinator.Should().NotContain("private static (Window Window, ProgressBar Bar");

    var factory = SourceScanHelper.ReadSource("src/SmartGuard.Settings/UpdateDownloadProgressWindowFactory.cs");
    factory.Should().Contain("Create");
    factory.Should().Contain("ProgressBar");
  }
}
