using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class TrayLifecycleArchitectureTests
{
  [Fact]
  public void TrayLifecycle_must_exist_as_single_restart_source()
  {
    File.Exists(Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Configuration",
      "TrayLifecycle.cs")).Should().BeTrue();

    var lifecycle = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/TrayLifecycle.cs");
    lifecycle.Should().Contain("TrayLifecycle");
    lifecycle.Should().Contain("TrayLauncher");
    lifecycle.Should().Contain("SmartGuardPaths.TrayExeFileName");
    lifecycle.Should().NotContain("SmartGuard.Engine");
  }

}
