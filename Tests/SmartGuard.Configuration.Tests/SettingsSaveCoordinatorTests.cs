namespace SmartGuard.Configuration.Tests;

public class SettingsSaveCoordinatorTests
{
  [Fact]
  public void Save_writes_config_and_preserves_unknown_keys()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.config.json");
    File.WriteAllText(path, """{"BalancedThresholdSec":300,"PowerSaverThresholdSec":900,"CustomFlag":true}""");

    try
    {
      var repo = new GuardConfigRepository(path);
      var previous = repo.TryLoad()!;
      var updated = SettingsSnapshotMapper.ApplyTraySettings(
        previous, 5, 15, 30, 15, 300, previous.HeartbeatIntervalMin,
        previous.ActivePlanGuid, previous.BalancedPlanGuid, previous.PowerSaverPlanGuid,
        false, true, true, true);

      SettingsSaveCoordinator.Save(updated, previous, dir, repo);

      var text = File.ReadAllText(path);
      text.Should().Contain("CustomFlag");
      text.Should().Contain("\"CheckIntervalSec\": 15");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }
}
