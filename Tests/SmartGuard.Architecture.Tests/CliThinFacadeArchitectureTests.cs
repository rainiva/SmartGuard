using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class CliThinFacadeArchitectureTests
{
  private static IEnumerable<string> CliSourceFiles()
  {
    var cliDir = Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Cli");
    Directory.Exists(cliDir).Should().BeTrue();
    return Directory.EnumerateFiles(cliDir, "*.cs", SearchOption.AllDirectories);
  }

  [Theory]
  [InlineData("schtasks")]
  [InlineData("taskkill")]
  [InlineData("RegisterGuardian")]
  [InlineData("RegisterTray")]
  [InlineData("Verb = \"runas\"")]
  [InlineData("SmartGuard.Engine.exe")]
  [InlineData("SmartGuard.Tray.exe")]
  [InlineData("SmartGuard.Settings.exe")]
  public void Cli_sources_must_not_contain_forbidden_operational_literals(string forbidden)
  {
    foreach (var file in CliSourceFiles())
    {
      var source = File.ReadAllText(file);
      source.Should().NotContain(
        forbidden,
        $"{file} must delegate operations to SmartGuard.Configuration, not embed {forbidden}");
    }
  }
}
