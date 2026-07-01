using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsLogPageHostArchitectureTests
{
  [Fact]
  public void Log_page_behavior_lives_in_SettingsLogPageHost()
  {
    var hostPath = Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Settings",
      "SettingsLogPageHost.cs");
    File.Exists(hostPath).Should().BeTrue("log page wiring must be extracted to SettingsLogPageHost");

    var hostSource = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsLogPageHost.cs");
    hostSource.Should().Contain("EnsureLogViewInitialized");
    hostSource.Should().Contain("RefreshLogView");
    hostSource.Should().Contain("SetLogPageActive");

    var controllerSource = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsWindowController.cs");
    controllerSource.Should().Contain("SettingsLogPageHost");
    controllerSource.Should().NotContain("private void EnsureLogViewInitialized");
    controllerSource.Should().NotContain("private void RefreshLogView");
  }
}
