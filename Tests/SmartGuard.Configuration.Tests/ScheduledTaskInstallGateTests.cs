using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class ScheduledTaskInstallGateTests
{
  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("0")]
  [InlineData("false")]
  [InlineData("no")]
  public void ShouldSkip_false_when_no_flag_and_env_not_truthy(string? env)
  {
    ScheduledTaskInstallGate.ShouldSkip(cliFlag: false, envValue: env).Should().BeFalse();
  }

  [Theory]
  [InlineData("1")]
  [InlineData("true")]
  [InlineData("TRUE")]
  [InlineData("yes")]
  [InlineData("on")]
  public void ShouldSkip_true_when_env_truthy(string env)
  {
    ScheduledTaskInstallGate.ShouldSkip(cliFlag: false, envValue: env).Should().BeTrue();
  }

  [Fact]
  public void ShouldSkip_true_when_cli_flag_set_even_if_env_empty()
  {
    ScheduledTaskInstallGate.ShouldSkip(cliFlag: true, envValue: null).Should().BeTrue();
  }

  [Fact]
  public void EnvVarName_is_stable()
  {
    ScheduledTaskInstallGate.EnvVarName.Should().Be("SMARTGUARD_SKIP_SCHEDULED_TASKS");
  }
}
