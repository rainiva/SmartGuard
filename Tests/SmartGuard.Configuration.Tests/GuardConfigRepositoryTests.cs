namespace SmartGuard.Configuration.Tests;

public class GuardConfigRepositoryTests
{
  [Fact]
  public void Save_preserves_unknown_json_keys()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.config.json");
    File.WriteAllText(path, """
      {
        "BalancedThresholdSec": 300,
        "PowerSaverThresholdSec": 900,
        "LowBatteryPercent": 30,
        "CheckIntervalSec": 15,
        "BrightnessRestoreMs": 300,
        "Paused": false,
        "CustomExperimentalFlag": true
      }
      """);

    try
    {
      var repo = new GuardConfigRepository(path);
      var loaded = repo.TryLoad();
      loaded.Should().NotBeNull();
      loaded!.Paused = true;
      repo.Save(loaded);

      File.ReadAllText(path).Should().Contain("CustomExperimentalFlag");
      File.ReadAllText(path).Should().Contain("\"Paused\": true");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }
}
