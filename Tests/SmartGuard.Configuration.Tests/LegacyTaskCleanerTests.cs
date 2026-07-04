using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LegacyTaskCleanerTests
{
    [Fact]
    public void CleanLegacyTasks_should_not_throw_when_no_legacy_tasks_exist()
    {
        Action act = () => LegacyTaskCleaner.CleanLegacyTasks();
        act.Should().NotThrow();
    }

    [Fact]
    public void CleanLegacyTasks_should_use_catalog_task_names()
    {
        var cleanerSource = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "SmartGuard.Configuration", "LegacyTaskCleaner.cs"));
        cleanerSource.Should().Contain("LegacyScheduledTaskCatalog.TaskNames");
        cleanerSource.Should().NotContain("\"SmartPowerPlan Guardian\"");
        LegacyScheduledTaskCatalog.TaskNames.Should().Contain("SmartPowerPlan Guardian");
        LegacyScheduledTaskCatalog.TaskNames.Should().Contain("SmartPowerPlan Tray");
    }
}
