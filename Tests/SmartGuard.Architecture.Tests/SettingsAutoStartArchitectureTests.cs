using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsAutoStartArchitectureTests
{
  [Fact]
  public void SettingsPolicyCoordinator_must_sync_autostart_from_tasks_on_ui_load()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsPolicyCoordinator.cs");
    source.Should().Contain("AutoStartService.SyncFromTasks()");
    source.Should().NotMatchRegex(
      @"ApplyInitialValues[\s\S]*_tglAutoStart\.IsChecked\s*=\s*config\.AutoStartEnabled");
  }
}
