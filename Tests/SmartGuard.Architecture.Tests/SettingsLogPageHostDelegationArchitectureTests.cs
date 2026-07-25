using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsLogPageHostDelegationArchitectureTests
{
  [Fact]
  public void Log_page_host_delegates_control_initialization_to_a_dedicated_initializer()
  {
    var host = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsLogPageHost.cs");

    File.Exists(Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Settings",
      "SettingsLogPageInitializer.cs")).Should().BeTrue();
    host.Should().Contain("SettingsLogPageInitializer");
  }

  [Fact]
  public void Log_page_host_delegates_visible_log_actions_to_an_export_coordinator()
  {
    var host = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsLogPageHost.cs");

    File.Exists(Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Settings",
      "SettingsLogExportCoordinator.cs")).Should().BeTrue();
    host.Should().Contain("SettingsLogExportCoordinator");
  }
}
