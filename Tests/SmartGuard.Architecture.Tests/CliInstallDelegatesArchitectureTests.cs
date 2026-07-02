using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class CliInstallDelegatesArchitectureTests
{
  [Fact]
  public void Cli_router_install_commands_must_delegate_to_InstallCommands()
  {
    var router = SourceScanHelper.ReadSource("src/SmartGuard.Cli/CliRouter.cs");
    router.Should().Contain("InstallCommands.RunInstall");
    router.Should().Contain("InstallCommands.RunUninstall");
    router.Should().NotContain("ScheduledTaskRegistrar.RegisterGuardian");
    router.Should().NotContain("ScheduledTaskRegistrar.RegisterTray");
  }
}
