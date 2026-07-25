using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class BatteryStatusSourceArchitectureTests
{
  [Fact]
  public void Architecture_contract_must_register_native_battery_status_as_primary_source()
  {
    var contract = SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");

    contract.Should().Contain("BatteryInfoProvider");
    contract.Should().Contain("GetSystemPowerStatus");
    contract.Should().Contain("WMI");
  }
}
