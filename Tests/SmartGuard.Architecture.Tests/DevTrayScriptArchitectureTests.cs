using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class DevTrayScriptArchitectureTests
{
  private static readonly string[] EngineStopPatterns =
  [
    @"Stop-Process\s+-Name\s+SmartGuard\.Engine",
    @"taskkill\s+/F\s+/IM\s+SmartGuard\.Engine",
    @"Get-Process\s+-Name\s+'SmartGuard\.Engine'[\s\S]*Stop-Process",
  ];

  [Theory]
  [InlineData("Start-Tray.cmd")]
  [InlineData("Restart-Tray.cmd")]
  public void Dev_tray_scripts_must_be_marked_dev_only(string relativePath)
  {
    var content = SourceScanHelper.ReadSource(relativePath);
    content.Should().MatchRegex("(?i)(dev helper|dev-only|dev only)");
  }

  [Theory]
  [InlineData("Start-Tray.cmd")]
  [InlineData("Restart-Tray.cmd")]
  public void Dev_tray_scripts_must_launch_tray_exe_directly_not_schtasks(string relativePath)
  {
    var content = SourceScanHelper.ReadSource(relativePath);
    content.Should().Contain("SmartGuard.Tray.exe");
    content.Should().NotContain("schtasks /Run");
  }

  [Fact]
  public void Restart_Tray_must_not_kill_engine_outside_EngineLifecycle()
  {
    var content = SourceScanHelper.ReadSource("Restart-Tray.cmd");
    foreach (var pattern in EngineStopPatterns)
      content.Should().NotMatchRegex(pattern);
    content.Should().Contain("SmartGuard.Tray.exe");
  }
}
