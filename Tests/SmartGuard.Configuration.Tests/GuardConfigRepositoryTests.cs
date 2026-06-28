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

  [Fact]
  public void LoadOrDefault_migrates_missing_notify_on_external_change_from_plan_change()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.config.json");
    File.WriteAllText(path, """
      {
        "NotifyOnPlanChange": false
      }
      """);

    try
    {
      var repo = new GuardConfigRepository(path);
      var loaded = repo.LoadOrDefault(dir);

      loaded.NotifyOnPlanChange.Should().BeFalse();
      loaded.NotifyOnExternalChange.Should().BeFalse();
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void LoadOrDefault_defaults_theme_follow_system_when_missing()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.config.json");
    File.WriteAllText(path, """
      {
        "BalancedThresholdSec": 300
      }
      """);

    try
    {
      var repo = new GuardConfigRepository(path);
      var loaded = repo.LoadOrDefault(dir);

      loaded.ThemeFollowSystem.Should().BeTrue();
      loaded.ThemeIsDark.Should().BeFalse();
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void Save_persists_theme_preferences()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.config.json");

    try
    {
      var repo = new GuardConfigRepository(path);
      var config = GuardConfig.CreateDefault(dir);
      config.ThemeFollowSystem = false;
      config.ThemeIsDark = true;
      repo.Save(config);

      var reloaded = repo.LoadOrDefault(dir);
      reloaded.ThemeFollowSystem.Should().BeFalse();
      reloaded.ThemeIsDark.Should().BeTrue();
      File.ReadAllText(path).Should().Contain("\"ThemeFollowSystem\": false");
      File.ReadAllText(path).Should().Contain("\"ThemeIsDark\": true");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void Save_persists_notify_on_external_change()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.config.json");

    try
    {
      var repo = new GuardConfigRepository(path);
      var config = GuardConfig.CreateDefault(dir);
      config.NotifyOnPlanChange = true;
      config.NotifyOnExternalChange = false;
      repo.Save(config);

      var reloaded = repo.LoadOrDefault(dir);
      reloaded.NotifyOnPlanChange.Should().BeTrue();
      reloaded.NotifyOnExternalChange.Should().BeFalse();
      File.ReadAllText(path).Should().Contain("\"NotifyOnExternalChange\": false");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }
}
