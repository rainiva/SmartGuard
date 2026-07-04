using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class LegacyTaskCleanupArchitectureTests
{
  [Fact]
  public void LegacyTaskCleaner_must_consume_single_catalog_instead_of_inline_task_literals()
  {
    var cleaner = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/LegacyTaskCleaner.cs");
    cleaner.Should().Contain("LegacyScheduledTaskCatalog.TaskNames");
    cleaner.Should().NotContain("\"SmartPowerPlan Guardian\"");
    cleaner.Should().NotContain("\"SmartPowerPlan Tray\"");
  }

  [Fact]
  public void Live_scripts_must_not_keep_legacy_cleanup_entrypoints_or_name_lists()
  {
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, "scripts", "LegacyScheduledTaskNames.ps1")).Should().BeFalse();
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, "scripts", "Unregister-LegacySmartPowerPlanTasks.ps1")).Should().BeFalse();
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, "scripts", "archive", "LegacyScheduledTaskNames.ps1")).Should().BeTrue();
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, "scripts", "archive", "Unregister-LegacySmartPowerPlanTasks.ps1")).Should().BeTrue();
  }
}
