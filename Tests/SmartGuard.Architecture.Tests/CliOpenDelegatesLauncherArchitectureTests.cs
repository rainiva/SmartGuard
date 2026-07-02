using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class CliOpenDelegatesLauncherArchitectureTests
{
  [Fact]
  public void Cli_router_open_commands_must_delegate_to_launchers()
  {
    var router = string.Join(
      Environment.NewLine,
      Directory.EnumerateFiles(Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Cli"), "*.cs")
        .Select(File.ReadAllText));
    router.Should().Contain("SettingsMainPageLauncher.Open");
    router.Should().Contain("SettingsLogsPageLauncher.Open");
    router.Should().Contain("TrayLauncher.Start");
    router.Should().NotContain("Process.Start");
  }
}
