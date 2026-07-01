using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.Tests;

public class SettingsLogFilePathTests
{
  [Fact]
  public void ResolveLogFile_uses_custom_path_from_config()
  {
    var dir = Path.Combine(Path.GetTempPath(), "sg-log-" + Guid.NewGuid().ToString("N"));
    var custom = Path.Combine(dir, "logs", "custom.log");
    var config = GuardConfig.CreateDefault(dir);
    config.LogFile = custom;

    SmartGuardPaths.ResolveLogFile(config, dir).Should().Be(custom);
  }
}
