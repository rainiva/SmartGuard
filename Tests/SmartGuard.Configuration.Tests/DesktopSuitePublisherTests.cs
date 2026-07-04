using System.Diagnostics;
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

  [Fact]
  public void Publish_normalizes_root_argument_with_trailing_quote_from_cmd_shell_parsing()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-publish-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var badRoot = root + "\"";
    var captured = new List<ProcessStartInfo>();

    DesktopSuitePublisher.RunProcessForTests = psi =>
    {
      captured.Add(psi);
      return 0;
    };

    try
    {
      var exit = DesktopSuitePublisher.Publish(badRoot, "Release");

      exit.Should().Be(0);
      captured.Should().HaveCount(DesktopSuitePublisher.ProjectRelativePaths.Count);
      captured.Should().OnlyContain(psi => psi.WorkingDirectory == Path.GetFullPath(root));
      captured.Should().OnlyContain(psi => psi.Arguments.Contains($"\"{Path.Combine(root, "bin")}\"", StringComparison.Ordinal));
      captured.Should().OnlyContain(psi => !psi.Arguments.Contains(badRoot, StringComparison.Ordinal));
    }
    finally
    {
      DesktopSuitePublisher.RunProcessForTests = null;
      Directory.Delete(root, true);
    }
  }
}
