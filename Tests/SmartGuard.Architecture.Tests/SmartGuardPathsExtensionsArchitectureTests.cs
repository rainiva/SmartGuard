using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class SmartGuardPathsExtensionsArchitectureTests
{
  [Fact]
  public void SmartGuardPaths_must_not_expose_runtime_SettingsXaml_or_BrandIcon_paths()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SmartGuardPaths.cs");
    source.Should().NotContain("SettingsXamlFileName");
    source.Should().NotContain("BrandIcon(");
    SmartGuardPaths.InitMarkerFileName.Should().Be(".SmartGuard.initialized");
    SmartGuardPaths.ElevationDeclinedMarkerFileName.Should().Be(".SmartGuard.elevation-declined");

    var root = @"C:\SmartGuard";
    SmartGuardPaths.InitMarkerFile(root).Should().Be(Path.Combine(root, ".SmartGuard.initialized"));
    SmartGuardPaths.ElevationDeclinedMarkerFile(root).Should().Be(Path.Combine(root, ".SmartGuard.elevation-declined"));
  }

  [Fact]
  public void SettingsWindowController_must_use_embedded_settings_xaml_only()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsWindowController.cs");
    source.Should().Contain("SettingsXamlLoader.TryLoadEmbeddedWindow");
    source.Should().NotContain("TryLoadLooseWindowFromFile");
    source.Should().NotContain("SmartGuardPaths.SettingsXaml");
  }

  [Fact]
  public void AppBrandIcon_must_not_accept_install_root_inputs()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/AppBrandIcon.cs");
    source.Should().NotContain("installRoot");
    source.Should().NotContain("LoadImageSource(string");
    source.Should().NotContain("ApplyTo(Window window, string");
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
