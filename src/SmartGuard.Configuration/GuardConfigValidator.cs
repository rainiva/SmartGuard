namespace SmartGuard.Configuration;

public static class GuardConfigValidator
{
  public static IReadOnlyList<string> Validate(GuardConfig config)
  {
    var errors = new List<string>();
    if (config.BalancedThresholdSec < 60)
      errors.Add("平衡阈值至少 60 秒");
    if (config.PowerSaverThresholdSec <= config.BalancedThresholdSec)
      errors.Add("节能阈值必须大于平衡阈值");
    if (config.LowBatteryPercent is < 0 or > 100)
      errors.Add("低电量百分比须在 0~100");
    if (config.CheckIntervalSec < 5)
      errors.Add("轮询间隔至少 5 秒");
    if (config.BrightnessRestoreMs < 0)
      errors.Add("亮度恢复延迟不能为负");
    return errors;
  }
}
