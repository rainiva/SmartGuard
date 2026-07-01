using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class ScheduledTaskNamesContractTests
{
  [Fact]
  public void Status_cmd_queries_registrar_task_names_only()
  {
    var content = File.ReadAllText(Path.Combine(SourceScanHelper.RepoRoot, "Status.cmd"));
    content.Should().Contain(ScheduledTaskRegistrar.GuardianTaskName);
    content.Should().Contain(ScheduledTaskRegistrar.TrayTaskName);
    content.Should().NotContain("SmartPowerPlan");
  }

  [Fact]
  public void Inno_setup_uses_registrar_guardian_task_name()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    iss.Should().Contain(ScheduledTaskRegistrar.GuardianTaskName);
    iss.Should().Contain(ScheduledTaskRegistrar.TrayTaskName);
    iss.Should().NotContain("SmartPowerPlan Guardian");
  }
}
