using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class PowerCfgProcessRunnerTests
{
    [Fact]
    public void RunProcess_reports_timeout_and_kills_child_when_wait_exceeds_timeout()
    {
        var result = PowerCfgProcessRunner.RunProcess(
            "cmd.exe",
            "/c ping localhost -n 8 > nul",
            TimeSpan.FromSeconds(1));

        result.Started.Should().BeTrue();
        result.TimedOut.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void RunProcess_returns_output_for_fast_command()
    {
        var result = PowerCfgProcessRunner.RunProcess(
            "cmd.exe",
            "/c echo hello-powercfg-runner",
            TimeSpan.FromSeconds(5));

        result.Succeeded.Should().BeTrue();
        result.StandardOutput.Should().Contain("hello-powercfg-runner");
    }

    [Fact]
    public void RunProcess_reports_start_failure_without_faking_success()
    {
        var result = PowerCfgProcessRunner.RunProcess(
            "missing-powercfg-test-executable.exe",
            "",
            TimeSpan.FromSeconds(1));

        result.Started.Should().BeFalse();
        result.Succeeded.Should().BeFalse();
        result.ExitCode.Should().BeNull();
    }
}
