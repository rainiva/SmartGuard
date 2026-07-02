using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class SmartGuardPathsExtensionsArchitectureTests
{
  [Fact]
  public void SmartGuardPaths_must_expose_SettingsXaml_and_InitMarker()
  {
    SmartGuardPaths.SettingsXamlFileName.Should().Be("SmartGuard.Settings.xaml");
    SmartGuardPaths.InitMarkerFileName.Should().Be(".SmartGuard.initialized");
    SmartGuardPaths.ElevationDeclinedMarkerFileName.Should().Be(".SmartGuard.elevation-declined");

    var root = @"C:\SmartGuard";
    SmartGuardPaths.SettingsXaml(root).Should().Be(Path.Combine(root, "lib", "SmartGuard.Settings.xaml"));
    SmartGuardPaths.InitMarkerFile(root).Should().Be(Path.Combine(root, ".SmartGuard.initialized"));
    SmartGuardPaths.ElevationDeclinedMarkerFile(root).Should().Be(Path.Combine(root, ".SmartGuard.elevation-declined"));
  }

  [Fact]
  public void SettingsWindowController_must_use_SmartGuardPaths_SettingsXaml()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsWindowController.cs");
    source.Should().NotContain(
      "Path.Combine(root, \"lib\", \"SmartGuard.Settings.xaml\")",
      "use SmartGuardPaths.SettingsXaml");
    source.Should().Contain("SmartGuardPaths.SettingsXaml");
  }

  [Fact]
  public void Engine_Program_must_use_SmartGuardPaths_InitMarker()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Program.cs");
    source.Should().NotContain("\".SmartGuard.initialized\"", "use SmartGuardPaths.InitMarkerFile");
    source.Should().Contain("SmartGuardPaths.InitMarkerFile");
  }

  [Fact]
  public void ElevationDeclinedMarker_must_use_SmartGuardPaths()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/Cli/InstallCommands.cs");
    source.Should().NotContain(
      "\".SmartGuard.elevation-declined\"",
      "use SmartGuardPaths.ElevationDeclinedMarkerFile");
    source.Should().Contain("SmartGuardPaths.ElevationDeclinedMarkerFile");
  }
}
