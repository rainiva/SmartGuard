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
}
