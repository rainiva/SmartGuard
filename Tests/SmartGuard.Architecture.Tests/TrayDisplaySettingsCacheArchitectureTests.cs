using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class TrayDisplaySettingsCacheArchitectureTests
{
  [Fact]
  public void TrayApplicationContext_must_use_repository_backed_TrayDisplaySettingsCache()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Tray/TrayApplicationContext.cs");
    source.Should().Contain("new TrayDisplaySettingsCache(_configRepository, _root)");
    source.Should().NotContain("new TrayNotificationPreferences(config.NotifyOnPlanChange");
  }
}
