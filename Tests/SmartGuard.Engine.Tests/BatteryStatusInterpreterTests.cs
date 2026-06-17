using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class BatteryStatusInterpreterTests
{
  [Theory]
  [InlineData(BatteryStatusInterpreter.AcLineOnline, true)]
  [InlineData(BatteryStatusInterpreter.AcLineOffline, false)]
  [InlineData(BatteryStatusInterpreter.AcLineUnknown, null)]
  public void InterpretAcLineStatus_maps_windows_values(byte acLineStatus, bool? expected)
  {
    BatteryStatusInterpreter.InterpretAcLineStatus(acLineStatus).Should().Be(expected);
  }

  [Theory]
  [InlineData(72, 72)]
  [InlineData(0, 0)]
  [InlineData(100, 100)]
  [InlineData(BatteryStatusInterpreter.BatteryPercentUnknown, null)]
  public void InterpretBatteryLifePercent_maps_windows_values(byte batteryLifePercent, int? expected)
  {
    BatteryStatusInterpreter.InterpretBatteryLifePercent(batteryLifePercent).Should().Be(expected);
  }

  [Theory]
  [InlineData(6, true)]
  [InlineData(9, true)]
  [InlineData(4, false)]
  [InlineData(5, false)]
  [InlineData(11, false)]
  [InlineData(2, null)]
  [InlineData(3, null)]
  [InlineData(10, null)]
  public void InterpretWmiBatteryStatus_uses_charging_not_unknown(int batteryStatus, bool? expected)
  {
    BatteryStatusInterpreter.InterpretWmiBatteryStatus(batteryStatus).Should().Be(expected);
  }

  [Fact]
  public void AggregateEstimatedChargeRemaining_weights_by_design_capacity()
  {
    var batteries = new (int Percent, uint Weight)[]
    {
      (80, 50000),
      (40, 50000),
    };

    BatteryStatusInterpreter.AggregateEstimatedChargeRemaining(batteries).Should().Be(60);
  }

  [Fact]
  public void Resolve_prefers_system_percent_over_wmi()
  {
    var result = BatteryStatusInterpreter.Resolve(
      acLineStatus: BatteryStatusInterpreter.AcLineOffline,
      batteryLifePercent: 55,
      batteryFlag: 0,
      wmiPercent: 90,
      wmiOnAc: true);

    result.Should().Be((55, false));
  }

  [Fact]
  public void Resolve_falls_back_to_wmi_percent_when_system_unknown()
  {
    var result = BatteryStatusInterpreter.Resolve(
      acLineStatus: BatteryStatusInterpreter.AcLineOffline,
      batteryLifePercent: BatteryStatusInterpreter.BatteryPercentUnknown,
      batteryFlag: 0,
      wmiPercent: 42,
      wmiOnAc: null);

    result.Should().Be((42, false));
  }

  [Fact]
  public void Resolve_uses_wmi_ac_hint_when_ac_line_unknown()
  {
    var result = BatteryStatusInterpreter.Resolve(
      acLineStatus: BatteryStatusInterpreter.AcLineUnknown,
      batteryLifePercent: 80,
      batteryFlag: 0,
      wmiPercent: null,
      wmiOnAc: false);

    result.Should().Be((80, false));
  }

  [Fact]
  public void Resolve_returns_desktop_defaults_when_no_system_battery()
  {
    var result = BatteryStatusInterpreter.Resolve(
      acLineStatus: BatteryStatusInterpreter.AcLineOnline,
      batteryLifePercent: BatteryStatusInterpreter.BatteryPercentUnknown,
      batteryFlag: BatteryStatusInterpreter.BatteryFlagNoBattery,
      wmiPercent: null,
      wmiOnAc: null);

    result.Should().Be((100, true));
  }
}
