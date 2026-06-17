using SmartGuard.Configuration;

namespace SmartGuard.Settings;

public static class SettingsInitialValues
{
  public static int BalancedThresholdMinutes(GuardConfig config)
    => Math.Max(1, config.BalancedThresholdSec / 60);

  public static int PowerSaverThresholdMinutes(GuardConfig config)
    => Math.Max(2, config.PowerSaverThresholdSec / 60);
}
