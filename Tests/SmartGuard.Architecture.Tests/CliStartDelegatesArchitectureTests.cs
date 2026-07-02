using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class CliStartDelegatesArchitectureTests
{
  [Fact]
  public void Cli_router_start_commands_must_delegate_to_configuration_ops()
  {
    var router = SourceScanHelper.ReadSource("src/SmartGuard.Cli/CliRouter.cs");
    router.Should().Contain("GuardianTaskRunner.Run");
    router.Should().Contain("EngineLauncher.StartForeground");
    router.Should().Contain("TrayLauncher.Start");
    router.Should().Contain("TrayLifecycle.Restart");
    router.Should().Contain("StatusReporter.FormatStatus");
    router.Should().NotContain("Process.Start");
  }
}
