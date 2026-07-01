using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.Tests;

public class SettingsThemeSaveCoordinatorTests
{
  [Fact]
  public void SaveThemePreferences_should_merge_ui_thresholds_not_stale_original()
  {
    var dir = Path.Combine(Path.GetTempPath(), "sg-theme-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = SmartGuardPaths.ConfigFile(dir);
    var repository = new GuardConfigRepository(path);
    var original = GuardConfig.CreateDefault(dir);
    original.BalancedThresholdSec = 300;
    repository.Save(original);

    try
    {
      var fromUi = SettingsSnapshotMapper.ApplyTraySettings(
        original,
        balancedThresholdMin: 10,
        powerSaverThresholdMin: 20,
        lowBatteryPercent: original.LowBatteryPercent,
        checkIntervalSec: original.CheckIntervalSec,
        brightnessRestoreMs: original.BrightnessRestoreMs,
        heartbeatIntervalMin: original.HeartbeatIntervalMin,
        activePlanGuid: original.ActivePlanGuid,
        balancedPlanGuid: original.BalancedPlanGuid,
        powerSaverPlanGuid: original.PowerSaverPlanGuid,
        paused: original.Paused,
        notifyOnPlanChange: original.NotifyOnPlanChange,
        notifyOnExternalChange: original.NotifyOnExternalChange,
        autoStartEnabled: original.AutoStartEnabled);

      fromUi.ThemeFollowSystem = true;
      fromUi.ThemeIsDark = true;
      SettingsSaveCoordinator.Save(fromUi, original, dir, repository);

      repository.TryLoad()!.BalancedThresholdSec.Should().Be(600,
        "theme save must persist debounced UI threshold changes");
    }
    finally
    {
      Directory.Delete(dir, true);
    }
  }
}
