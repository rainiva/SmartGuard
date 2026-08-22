using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class PowerLifecycleArchitectureTests
{
  [Fact]
  public void Contract_registers_single_power_lifecycle_owner()
  {
    var contract = SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");
    contract.Should().Contain("PowerLifecycleStateMachine");
    contract.Should().Contain("Suspend 期间禁止计划仲裁");
    contract.Should().Contain("Resume 建立新的 awake-idle epoch");
  }

  [Fact]
  public void Power_events_are_not_collapsed_into_ac_state()
  {
    SourceScanHelper.ReadSource(
        "src/SmartGuard.Engine/Infrastructure/PowerEventWakeListener.cs")
      .Should().Contain("PowerLifecycleSignal");
    File.Exists(Path.Combine(
        SourceScanHelper.RepoRoot,
        "src",
        "SmartGuard.Engine",
        "Infrastructure",
        "PowerEventStateResolver.cs")).Should().BeFalse();
  }

  [Fact]
  public void Policy_engine_remains_the_only_three_tier_rule_owner()
  {
    var lifecycle = SourceScanHelper.ReadSource(
      "src/SmartGuard.Engine/Infrastructure/PowerLifecycleStateMachine.cs");
    lifecycle.Should().NotContain("BalancedThresholdSec");
    lifecycle.Should().NotContain("PowerSaverThresholdSec");
    lifecycle.Should().NotContain("ActivePlanGuid");
  }
}
