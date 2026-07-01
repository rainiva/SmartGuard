using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsIdleArchitectureTests
{
  [Fact]
  public void Settings_must_not_define_second_idle_seconds_resolver()
  {
    var controller = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsWindowController.cs");
    controller.Should().NotContain("GetLastInputInfo");

    var host = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsLogPageHost.cs");
    host.Should().Contain("LogViewIdleReader");
  }
}
