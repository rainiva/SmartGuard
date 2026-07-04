using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class TaskNameSingleSourceArchitectureTests
{
  [Fact]
  public void GuardianTaskRunner_must_not_duplicate_guardian_task_name_literal()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/GuardianTaskRunner.cs");
    source.Should().NotContain(
      "\"SmartGuard Guardian\"",
      "use ScheduledTaskRegistrar.GuardianTaskName as the single source of truth");
    source.Should().Contain("ScheduledTaskRegistrar.GuardianTaskName");
  }

  [Fact]
  public void GuardianTaskRunner_schtasks_arguments_use_registrar_task_name()
  {
    GuardianTaskRunner.BuildSchTasksRunArguments()
      .Should().Contain(ScheduledTaskRegistrar.GuardianTaskName);
  }
}
