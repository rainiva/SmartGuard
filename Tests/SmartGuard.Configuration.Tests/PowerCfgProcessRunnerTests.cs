using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class PowerCfgProcessRunnerTests
{
    [Fact]
    public void RunProcess_kills_child_when_wait_exceeds_timeout()
    {
        var act = () => PowerCfgProcessRunner.RunProcess(
            "cmd.exe",
            "/c ping localhost -n 8 > nul",
            TimeSpan.FromSeconds(1));

        act.Should().Throw<TimeoutException>()
            .WithMessage("*timed out*");
    }

    [Fact]
    public void RunProcess_returns_output_for_fast_command()
    {
        var output = PowerCfgProcessRunner.RunProcess(
            "cmd.exe",
            "/c echo hello-powercfg-runner",
            TimeSpan.FromSeconds(5));

        output.Should().Contain("hello-powercfg-runner");
    }
}
