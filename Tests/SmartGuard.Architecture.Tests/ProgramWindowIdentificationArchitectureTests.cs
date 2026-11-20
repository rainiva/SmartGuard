using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ProgramWindowIdentificationArchitectureTests
{
  [Fact]
  public void Settings_reuses_the_single_foreground_reader_without_an_engine_project_dependency()
  {
    var project = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SmartGuard.Settings.csproj");
    project.Should().Contain("ForegroundProgramReader.cs").And.Contain("ForegroundObservation.cs");
    project.Should().NotContain("SmartGuard.Engine.csproj");
    var contract = SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");
    contract.Should().Contain("PROGRAM-WINDOW-IDENTIFICATION");
  }
}
