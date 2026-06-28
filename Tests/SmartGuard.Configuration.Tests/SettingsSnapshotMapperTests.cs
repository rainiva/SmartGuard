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
      heartbeatIntervalMin: current.HeartbeatIntervalMin,
      activePlanGuid: current.ActivePlanGuid,
      balancedPlanGuid: current.BalancedPlanGuid,
      powerSaverPlanGuid: current.PowerSaverPlanGuid,
      paused: false,
      notifyOnPlanChange: true,
      notifyOnExternalChange: true,
      autoStartEnabled: true);

    updated.BalancedThresholdSec.Should().Be(300);
    updated.PowerSaverThresholdSec.Should().Be(900);
    updated.ActivePlanGuid.Should().Be(current.ActivePlanGuid);
  }

  [Fact]
  public void ApplyTraySettings_preserves_manual_high_performance_until()
  {
    var current = GuardConfig.CreateDefault(@"D:\Project\SmartGuard");
    current.ManualHighPerformanceUntil = DateTime.Now.AddHours(1);

    var updated = SettingsSnapshotMapper.ApplyTraySettings(
      current,
      balancedThresholdMin: 5,
      powerSaverThresholdMin: 15,
      lowBatteryPercent: 30,
      checkIntervalSec: 15,
      brightnessRestoreMs: 300,
      heartbeatIntervalMin: current.HeartbeatIntervalMin,
      activePlanGuid: current.ActivePlanGuid,
      balancedPlanGuid: current.BalancedPlanGuid,
      powerSaverPlanGuid: current.PowerSaverPlanGuid,
      paused: false,
      notifyOnPlanChange: true,
      notifyOnExternalChange: true,
      autoStartEnabled: true);

    updated.ManualHighPerformanceUntil.Should().Be(current.ManualHighPerformanceUntil);
  }

  [Fact]
  public void ApplyTraySettings_updates_heartbeat_and_plan_guids()
  {
    var current = GuardConfig.CreateDefault(@"D:\Project\SmartGuard");
    var highPerf = Guid.Parse("11111111-1111-1111-1111-111111111111");
    var balanced = Guid.Parse("22222222-2222-2222-2222-222222222222");
    var saver = Guid.Parse("33333333-3333-3333-3333-333333333333");

    var updated = SettingsSnapshotMapper.ApplyTraySettings(
      current,
      balancedThresholdMin: 5,
      powerSaverThresholdMin: 15,
      lowBatteryPercent: 30,
      checkIntervalSec: 15,
      brightnessRestoreMs: 300,
      heartbeatIntervalMin: 20,
      activePlanGuid: highPerf,
      balancedPlanGuid: balanced,
      powerSaverPlanGuid: saver,
      paused: false,
      notifyOnPlanChange: true,
      notifyOnExternalChange: true,
      autoStartEnabled: true);

    updated.HeartbeatIntervalMin.Should().Be(20);
    updated.ActivePlanGuid.Should().Be(highPerf);
    updated.BalancedPlanGuid.Should().Be(balanced);
    updated.PowerSaverPlanGuid.Should().Be(saver);
  }

  [Fact]
  public void ApplyTraySettings_maps_notification_switches_independently()
  {
    var current = GuardConfig.CreateDefault(@"D:\Project\SmartGuard");

    var updated = SettingsSnapshotMapper.ApplyTraySettings(
      current,
      balancedThresholdMin: 5,
      powerSaverThresholdMin: 15,
      lowBatteryPercent: 30,
      checkIntervalSec: 15,
      brightnessRestoreMs: 300,
      heartbeatIntervalMin: current.HeartbeatIntervalMin,
      activePlanGuid: current.ActivePlanGuid,
      balancedPlanGuid: current.BalancedPlanGuid,
      powerSaverPlanGuid: current.PowerSaverPlanGuid,
      paused: false,
      notifyOnPlanChange: true,
      notifyOnExternalChange: false,
      autoStartEnabled: true);

    updated.NotifyOnPlanChange.Should().BeTrue();
    updated.NotifyOnExternalChange.Should().BeFalse();
  }
}
