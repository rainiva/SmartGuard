using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ElevationSingleSourceArchitectureTests
{
  [Fact]
  public void Start_Core_and_Debug_Engine_must_not_inline_VBS_elevation()
  {
    foreach (var relativePath in new[] { "Start-Core.cmd", "Debug-Engine.cmd" })
    {
      var content = SourceScanHelper.ReadSource(relativePath);
      content.Should().NotContain(
        "CreateObject(\"Shell.Application\")",
        $"{relativePath} must not inline VBS UAC elevation");
    }
  }

  [Fact]
  public void Start_Core_and_Debug_Engine_must_call_shared_elevation_script()
  {
    foreach (var relativePath in new[] { "Start-Core.cmd", "Debug-Engine.cmd" })
    {
      var content = SourceScanHelper.ReadSource(relativePath);
      content.Should().Contain(
        "Request-AdminElevation.cmd",
        $"{relativePath} must delegate UAC to scripts\\Request-AdminElevation.cmd");
    }
  }

  [Fact]
  public void InstallCommands_must_use_ElevationHelper()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/Cli/InstallCommands.cs");
    source.Should().Contain("ElevationHelper");
    source.Should().NotContain("Verb = \"runas\"");
  }

  [Fact]
  public void UpdateInstallerLauncher_must_use_ElevationHelper()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/UpdateInstallerLauncher.cs");
    source.Should().Contain("ElevationHelper");
    source.Should().NotContain("Verb = \"runas\"");
  }
}
