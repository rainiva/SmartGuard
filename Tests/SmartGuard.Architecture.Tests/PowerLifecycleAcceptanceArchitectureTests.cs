using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class PowerLifecycleAcceptanceArchitectureTests
{
  [Fact]
  public void Power_lifecycle_acceptance_has_one_observable_script_entry()
  {
    var script = SourceScanHelper.ReadSource("scripts/Invoke-PowerLifecycleAcceptance.ps1");
    script.Should().Contain("power-lifecycle-acceptance.result.json");
    script.Should().Contain("Start-Transcript");
    script.Should().Contain("result = 'Blocked'");
    script.Should().Contain("pipeline-health-delta");
    script.Should().Contain("status -eq 'Blocked'");
    script.Should().NotContain("SetSuspendState");
    script.Should().NotContain("Set-Date");
    SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md")
      .Should().Contain("PWR-LIFE-ACCEPT");
  }
}
