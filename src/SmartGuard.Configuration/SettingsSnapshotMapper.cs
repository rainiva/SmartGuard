namespace SmartGuard.Configuration;

public static class SettingsSnapshotMapper
{
  public static GuardConfig ApplyTraySettings(
    GuardConfig current,
    int balancedThresholdMin,
    int powerSaverThresholdMin,
    int lowBatteryPercent,
    int checkIntervalSec,
    int brightnessRestoreMs,
    bool paused,
    bool notifyOnPlanChange,
    bool autoStartEnabled)
  {
    return new GuardConfig
    {
      ActivePlanGuid = current.ActivePlanGuid,
      BalancedPlanGuid = current.BalancedPlanGuid,
      PowerSaverPlanGuid = current.PowerSaverPlanGuid,
      BalancedThresholdSec = balancedThresholdMin * 60,
      PowerSaverThresholdSec = powerSaverThresholdMin * 60,
      LowBatteryPercent = lowBatteryPercent,
      CheckIntervalSec = checkIntervalSec,
      BrightnessRestoreMs = brightnessRestoreMs,
      LogFile = current.LogFile,
      Paused = paused,
      LogMaxBytes = current.LogMaxBytes,
      BrightnessRetryCount = current.BrightnessRetryCount,
      BrightnessRetryDelayMs = current.BrightnessRetryDelayMs,
      NotifyOnPlanChange = notifyOnPlanChange,
      HeartbeatIntervalMin = current.HeartbeatIntervalMin,
      AutoStartEnabled = autoStartEnabled,
      ManualHighPerformanceUntil = current.ManualHighPerformanceUntil,
    };
  }
}
