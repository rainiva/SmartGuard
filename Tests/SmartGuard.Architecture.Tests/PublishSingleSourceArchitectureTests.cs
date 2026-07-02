using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class PublishSingleSourceArchitectureTests
{
  [Fact]
  public void DesktopSuitePublisher_must_be_the_only_dotnet_publish_orchestrator()
  {
    File.Exists(Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Configuration",
      "Build",
      "DesktopSuitePublisher.cs")).Should().BeTrue();

    var buildCmd = SourceScanHelper.ReadSource("build.cmd");
    buildCmd.Should().NotContain("dotnet publish");
    buildCmd.Should().Contain("SmartGuard.Cli.exe");
    buildCmd.Should().Contain("publish");
  }

  [Theory]
  [InlineData("src/SmartGuard.Packaging/Commands/StageCommand.cs")]
  [InlineData("src/SmartGuard.Cli/CliRouter.cs")]
  public void Publish_callers_must_delegate_to_DesktopSuitePublisher(string relativePath)
  {
    var source = SourceScanHelper.ReadSource(relativePath);
    if (source.Contains("dotnet publish", StringComparison.Ordinal))
    {
      source.Should().Contain(
        "DesktopSuitePublisher",
        $"{relativePath} must not orchestrate dotnet publish directly");
    }
  }
}
