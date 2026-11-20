namespace SmartGuard.Configuration.Tests;

public class GuardConfigResetServiceTests
{
  [Fact]
  public void CreateResetConfig_restores_defaults_but_preserves_log_file()
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuardResetTest");
    var current = GuardConfig.CreateDefault(root);
    current.LogFile = @"D:\Custom\SmartGuard.log";
    current.BalancedThresholdSec = 120;
    current.ManualHighPerformanceUntil = DateTime.Now.AddHours(2);

    var reset = GuardConfigResetService.CreateResetConfig(current, root);

    reset.LogFile.Should().Be(current.LogFile);
    reset.BalancedThresholdSec.Should().Be(300);
    reset.ManualHighPerformanceUntil.Should().BeNull();
  }
}
