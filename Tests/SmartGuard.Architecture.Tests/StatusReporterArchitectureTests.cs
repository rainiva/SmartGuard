using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class StatusReporterArchitectureTests
{
  [Fact]
  public void StatusReporter_must_query_tasks_using_registrar_task_names()
  {
    var content = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/StatusReporter.cs");
    content.Should().Contain("ScheduledTaskRegistrar.TaskNames");
    content.Should().Contain("SmartGuardPaths.StartupLogFile");
    content.Should().NotContain("SmartPowerPlan");
  }

  [Fact]
  public void Status_cmd_must_not_be_revived_at_repo_root()
  {
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, "Status.cmd"))
      .Should().BeFalse("status is served by SmartGuard.Cli.exe status via StatusReporter");
  }

  [Fact]
  public void Cli_status_must_delegate_to_StatusReporter()
  {
    var cli = string.Join(
      Environment.NewLine,
      Directory.EnumerateFiles(Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Cli"), "*.cs")
        .Select(File.ReadAllText));
    cli.Should().Contain("StatusReporter.FormatStatus");
  }
}
