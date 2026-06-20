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

  [Fact]
  public void Save_is_idempotent_when_content_unchanged()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.config.json");

    try
    {
      var repo = new GuardConfigRepository(path);
      var config = GuardConfig.CreateDefault(dir);
      repo.Save(config);

      var firstWriteTime = File.GetLastWriteTimeUtc(path);
      Thread.Sleep(50);

      repo.Save(config);
      var secondWriteTime = File.GetLastWriteTimeUtc(path);

      secondWriteTime.Should().Be(firstWriteTime,
        "Save should be idempotent: writing the same config must not touch the file again.");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }
}
