using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class LegacyTaskCleanupArchitectureTests
{
  [Fact]
  public void Unregister_legacy_script_must_use_shared_legacy_task_name_constants()
  {
    var content = SourceScanHelper.ReadSource("scripts/Unregister-LegacySmartPowerPlanTasks.ps1");
    content.Should().Contain("LegacyScheduledTaskNames.ps1");
    content.Should().NotContain("'SmartGuard Guardian'");
    content.Should().NotContain("'SmartGuard Tray'");
  }
}
