using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class InstallCommandsLocationArchitectureTests
{
  [Fact]
  public void InstallCommands_must_live_in_Configuration_not_Engine()
  {
    File.Exists(Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Configuration",
      "Cli",
      "InstallCommands.cs")).Should().BeTrue();

    File.Exists(Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Engine",
      "Cli",
      "InstallCommands.cs")).Should().BeFalse();
  }

  [Fact]
  public void Engine_must_not_duplicate_install_register_logic()
  {
    var engineCliDir = Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Engine", "Cli");
    if (Directory.Exists(engineCliDir))
    {
      foreach (var file in Directory.EnumerateFiles(engineCliDir, "*.cs"))
      {
        var source = File.ReadAllText(file);
        source.Should().NotContain(
          "ScheduledTaskRegistrar.RegisterGuardian",
          because: $"{file} must not duplicate install register logic");
        source.Should().NotContain(
          "ScheduledTaskRegistrar.RegisterTray",
          because: $"{file} must not duplicate install register logic");
      }
    }
  }

  [Fact]
  public void Engine_program_must_delegate_install_to_Configuration_InstallCommands()
  {
    var program = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Program.cs");
    program.Should().Contain("InstallCommands.RunInstall");
    program.Should().Contain("InstallCommands.RunUninstall");
    program.Should().NotContain("SmartGuard.Engine.Cli.InstallCommands");
  }
}
