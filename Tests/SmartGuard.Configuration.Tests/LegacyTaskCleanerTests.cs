using FluentAssertions;

namespace SmartGuard.Configuration.Tests;

public class LegacyTaskCleanerTests
{
    [Fact]
    public void CleanLegacyTasks_should_not_throw_when_no_legacy_tasks_exist()
    {
        Action act = () => LegacyTaskCleaner.CleanLegacyTasks();
        act.Should().NotThrow();
    }
}
