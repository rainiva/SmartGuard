using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class GuardianTaskRunnerArchitectureTests
{
  [Fact]
  public void GuardianRecovery_must_delegate_to_GuardianTaskRunner_not_inline_schtasks()
  {
    var recovery = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/GuardianRecovery.cs");
    recovery.Should().Contain("GuardianTaskRunner");
    recovery.Should().NotContain("schtasks.exe");
  }

  [Fact]
  public void GuardianTaskRunner_is_single_schtasks_run_source()
  {
    File.Exists(Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Configuration",
      "GuardianTaskRunner.cs")).Should().BeTrue();

    var runner = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/GuardianTaskRunner.cs");
    runner.Should().Contain("schtasks.exe");
    runner.Should().Contain("ScheduledTaskRegistrar.GuardianTaskName");
    runner.Should().NotContain("\"SmartGuard Guardian\"");
  }

  [Fact]
  public void BuildSchTasksRunArguments_lives_on_GuardianTaskRunner()
  {
    GuardianTaskRunner.BuildSchTasksRunArguments()
      .Should().Contain(ScheduledTaskRegistrar.GuardianTaskName);
  }
}
