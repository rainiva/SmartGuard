namespace SmartGuard.Configuration.Tests;

public class GuardConfigValidatorTests
{
  [Fact]
  public void Validate_rejects_power_saver_not_above_balanced()
  {
    var config = new GuardConfig
    {
      BalancedThresholdSec = 300,
      PowerSaverThresholdSec = 200,
      LowBatteryPercent = 30,
      CheckIntervalSec = 15,
      BrightnessRestoreMs = 300,
    };

    GuardConfigValidator.Validate(config)
      .Should().Contain("节能阈值必须大于平衡阈值");
  }

  [Fact]
  public void Validate_rejects_balanced_below_sixty_seconds()
  {
    var config = new GuardConfig
    {
      BalancedThresholdSec = 30,
      PowerSaverThresholdSec = 900,
      LowBatteryPercent = 30,
      CheckIntervalSec = 15,
      BrightnessRestoreMs = 300,
    };

    GuardConfigValidator.Validate(config)
      .Should().Contain("平衡阈值至少 60 秒");
  }
}
