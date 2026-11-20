using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

// Integration tests start a real Engine against this machine. Every such root must be seeded first so the
// Engine skips the one-time brightness alignment and never switches the active plan (tests only read).
public class IntegrationEngineRootArchitectureTests
{
  private const string Seed = "Initialize-SmartGuardReadOnlyEngineRoot";

  [Fact]
  public void Tray_integration_seeds_every_root_before_starting_the_engine()
  {
    var source = SourceScanHelper.ReadSource("Tests/Integration/TrayCoreUserFlow.Tests.ps1");
    var blocks = source.Split("    It '").Skip(1).ToArray();
    blocks.Should().NotBeEmpty();
    foreach (var block in blocks)
    {
      var start = block.IndexOf("Start-Process -FilePath $engine", StringComparison.Ordinal);
      if (start < 0) continue;
      var seed = block.IndexOf(Seed, StringComparison.Ordinal);
      seed.Should().BeInRange(0, start, $"the root must be seeded before the engine starts in: It '{block[..Math.Min(80, block.Length)]}");
    }
  }

  [Fact]
  public void Installer_integration_seeds_the_root_before_running_setup()
  {
    var helpers = SourceScanHelper.ReadSource("Tests/Integration/InstallerUserFlow.Helpers.ps1");
    var body = helpers[helpers.IndexOf("function Invoke-SmartGuardSilentInstall", StringComparison.Ordinal)..];
    var setup = body.IndexOf("Start-Process -FilePath $SetupExe", StringComparison.Ordinal);
    setup.Should().BePositive();
    body.IndexOf(Seed, StringComparison.Ordinal).Should().BeInRange(0, setup,
      "setup starts the guardian task, which runs a real Engine in this root");
  }

  [Fact]
  public void Both_integration_helpers_load_the_shared_seed()
  {
    foreach (var path in new[] { "Tests/Integration/TrayCoreUserFlow.Helpers.ps1", "Tests/Integration/InstallerUserFlow.Helpers.ps1" })
      SourceScanHelper.ReadSource(path).Should().Contain("'ReadOnlyEngineRoot.ps1'", $"{path} must use the single seed helper");
  }
}
