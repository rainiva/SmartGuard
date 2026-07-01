using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class StatusCmdArchitectureTests
{
  [Fact]
  public void Status_cmd_must_query_tasks_using_registrar_task_names()
  {
    var content = SourceScanHelper.ReadSource("Status.cmd");
    content.Should().Contain(ScheduledTaskRegistrar.GuardianTaskName);
    content.Should().Contain(ScheduledTaskRegistrar.TrayTaskName);
    content.Should().NotContain("SmartPowerPlan");
  }
}
