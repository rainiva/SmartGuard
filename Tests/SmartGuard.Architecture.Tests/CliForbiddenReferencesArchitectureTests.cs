using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class CliForbiddenReferencesArchitectureTests
{
  [Fact]
  public void Cli_project_must_only_reference_Configuration()
  {
    var csproj = SourceScanHelper.ReadSource("src/SmartGuard.Cli/SmartGuard.Cli.csproj");
    csproj.Should().Contain("SmartGuard.Configuration.csproj");
    csproj.Should().NotContain("SmartGuard.Engine.csproj");
    csproj.Should().NotContain("SmartGuard.Tray.csproj");
    csproj.Should().NotContain("SmartGuard.Settings.csproj");
  }
}
