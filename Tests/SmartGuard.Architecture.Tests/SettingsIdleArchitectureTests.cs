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
    var refresher = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsLogViewRefresher.cs");
    (host + refresher).Should().Contain("LogViewIdleReader");
  }

  [Fact]
  public void Log_view_refresher_must_preserve_horizontal_offset_across_refreshes()
  {
    var viewport = SourceScanHelper.ReadSource("src/SmartGuard.Settings/LogViewportController.cs");

    viewport.Should().Contain("_preferredHorizontalOffset");
    viewport.Should().Contain("RestorePreferredHorizontalOffset");
    viewport.Should().Contain("ScrollToHorizontalOffset");
  }
}

