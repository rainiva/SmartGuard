namespace SmartGuard.Configuration.Tests;

public class SettingsSnapshotMapperTests
{
  [Fact]
  public void ApplyTraySettings_converts_minutes_to_seconds()
  {
    var current = GuardConfig.CreateDefault(@"D:\Project\SmartGuard");
    var updated = SettingsSnapshotMapper.ApplyTraySettings(
      current,
      balancedThresholdMin: 5,
      powerSaverThresholdMin: 15,
      lowBatteryPercent: 30,
      checkIntervalSec: 15,
      brightnessRestoreMs: 300,
      paused: false,
      notifyOnPlanChange: true,
      autoStartEnabled: true);

    updated.BalancedThresholdSec.Should().Be(300);
    updated.PowerSaverThresholdSec.Should().Be(900);
    updated.ActivePlanGuid.Should().Be(current.ActivePlanGuid);
  }
}
