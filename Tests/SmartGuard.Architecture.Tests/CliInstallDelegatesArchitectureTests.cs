using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class CliInstallDelegatesArchitectureTests
{
  [Fact]
  public void Cli_router_install_commands_must_delegate_to_InstallCommands()
  {
    var router = string.Join(
      Environment.NewLine,
      Directory.EnumerateFiles(Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Cli"), "*.cs")
        .Select(File.ReadAllText));
    router.Should().Contain("InstallCommands.RunInstall");
    router.Should().Contain("InstallCommands.RunUninstall");
    router.Should().NotContain("ScheduledTaskRegistrar.RegisterGuardian");
    router.Should().NotContain("ScheduledTaskRegistrar.RegisterTray");
  }
}
