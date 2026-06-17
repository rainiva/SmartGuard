using Microsoft.Win32;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class PowerEventInterpreterTests
{
  [Theory]
  [InlineData(PowerModes.Resume, true)]
  [InlineData(PowerModes.Suspend, false)]
  [InlineData(PowerModes.StatusChange, null)]
  public void InterpretPowerMode_maps_resume_and_suspend(PowerModes mode, bool? expected)
  {
    PowerEventInterpreter.InterpretPowerMode(mode).Should().Be(expected);
  }
}
