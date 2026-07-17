using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class InstallCommandsSkipArchitectureTests
{
  [Fact]
  public void InstallCommands_must_honor_scheduled_task_install_gate()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/Cli/InstallCommands.cs");
    source.Should().Contain("ScheduledTaskInstallGate.ShouldSkipFromEnvironment");
    source.Should().Contain("skipping scheduled task registration and desktop shell");
    var gateIdx = source.IndexOf("ShouldSkipFromEnvironment", StringComparison.Ordinal);
    var registerIdx = source.IndexOf("RegisterGuardian", StringComparison.Ordinal);
    gateIdx.Should().BeGreaterThan(-1);
    registerIdx.Should().BeGreaterThan(gateIdx);
  }

  [Fact]
  public void InstallCommands_must_forward_skip_scheduled_tasks_on_elevation()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/Cli/InstallCommands.cs");
    source.Should().Contain("--skip-scheduled-tasks");
    source.Should().Contain("skipScheduledTasks");
  }
}
