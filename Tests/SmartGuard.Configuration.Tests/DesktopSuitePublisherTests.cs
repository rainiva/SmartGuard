using SmartGuard.Configuration.Build;

namespace SmartGuard.Configuration.Tests;

public class DesktopSuitePublisherTests
{
  [Fact]
  public void ProjectRelativePaths_includes_five_desktop_exes()
  {
    DesktopSuitePublisher.ProjectRelativePaths
      .Should().HaveCount(5);
    DesktopSuitePublisher.ProjectRelativePaths
      .Should().Contain("src/SmartGuard.Cli/SmartGuard.Cli.csproj");
  }
}
