using SmartGuard.Configuration;

namespace SmartGuard.Settings.Tests;

public class GuardConfigResetServiceIntegrationTests
{
  [Fact]
  public void ResetToDefaults_clears_custom_thresholds_and_manual_boost()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuardResetUi", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var configPath = Path.Combine(dir, "SmartGuard.config.json");

    try
    {
      var repository = new GuardConfigRepository(configPath);
      var config = GuardConfig.CreateDefault(dir);
      config.BalancedThresholdSec = 120;
      config.ManualHighPerformanceUntil = DateTime.Now.AddHours(1);
      repository.Save(config);

      var resetConfig = GuardConfigResetService.CreateResetConfig(config, dir);
      SettingsSaveCoordinator.Save(resetConfig, config, dir, repository);

      var loaded = repository.TryLoad();
      loaded.Should().NotBeNull();
      loaded!.BalancedThresholdSec.Should().Be(300);
      loaded.ManualHighPerformanceUntil.Should().BeNull();
      loaded.LogFile.Should().Be(config.LogFile);
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }
}
