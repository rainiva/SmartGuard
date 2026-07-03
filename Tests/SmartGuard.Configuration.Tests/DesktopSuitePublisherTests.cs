using SmartGuard.Configuration.Build;

namespace SmartGuard.Configuration.Tests;

public class DesktopSuitePublisherTests
{
  [Fact]
  public void ProjectRelativePaths_includes_four_desktop_components_and_no_logviewer_host()
  {
    DesktopSuitePublisher.ProjectRelativePaths
      .Should().HaveCount(4);
    DesktopSuitePublisher.ProjectRelativePaths
      .Should().Contain("src/SmartGuard.Engine/SmartGuard.Engine.csproj");
    DesktopSuitePublisher.ProjectRelativePaths
      .Should().Contain("src/SmartGuard.Tray/SmartGuard.Tray.csproj");
    DesktopSuitePublisher.ProjectRelativePaths
      .Should().Contain("src/SmartGuard.Settings/SmartGuard.Settings.csproj");
    DesktopSuitePublisher.ProjectRelativePaths
      .Should().Contain("src/SmartGuard.Cli/SmartGuard.Cli.csproj");
    DesktopSuitePublisher.ProjectRelativePaths
      .Should().NotContain("src/SmartGuard.LogViewer/SmartGuard.LogViewer.csproj");
  }
}
