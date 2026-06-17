using SmartGuard.Configuration;

namespace SmartGuard.Settings.Tests;

public class SettingsInitialValuesTests
{
  [Fact]
  public void BalancedThresholdMinutes_uses_minimum_one()
  {
    var config = new GuardConfig { BalancedThresholdSec = 30 };
    SettingsInitialValues.BalancedThresholdMinutes(config).Should().Be(1);
  }

  [Fact]
  public void PowerSaverThresholdMinutes_uses_minimum_two()
  {
    var config = new GuardConfig { PowerSaverThresholdSec = 60 };
    SettingsInitialValues.PowerSaverThresholdMinutes(config).Should().Be(2);
  }
}
