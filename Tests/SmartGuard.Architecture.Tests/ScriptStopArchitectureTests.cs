using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ScriptStopArchitectureTests
{
  private static readonly string[] EngineStopPatterns =
  [
    @"Stop-Process\s+-Name\s+SmartGuard\.Engine",
    @"taskkill\s+/F\s+/IM\s+SmartGuard\.Engine",
    @"Get-Process\s+-Name\s+'SmartGuard\.Engine'[\s\S]*Stop-Process",
  ];

  [Theory]
  [InlineData("Restart-Tray.cmd")]
  [InlineData("scripts/Measure-EngineStartup.ps1")]
  public void Key_scripts_must_not_kill_engine_outside_EngineLifecycle(string relativePath)
  {
    var content = SourceScanHelper.ReadSource(relativePath);
    foreach (var pattern in EngineStopPatterns)
      content.Should().NotMatchRegex(pattern, $"{relativePath} must delegate engine stop to SmartGuardStop.ps1 / --uninstall");
  }

  [Fact]
  public void Measure_EngineStartup_must_delegate_stop_to_shared_helper()
  {
    var content = SourceScanHelper.ReadSource("scripts/Measure-EngineStartup.ps1");
    content.Should().Contain("SmartGuardStop.ps1");
    content.Should().Contain("Stop-SmartGuardProcesses");
  }

  [Fact]
  public void TrayCoreUserFlow_helpers_must_delegate_engine_stop()
  {
    var content = SourceScanHelper.ReadSource("Tests/Integration/TrayCoreUserFlow.Helpers.ps1");
    content.Should().Contain("Stop-SmartGuardForIntegrationTest");
    content.Should().NotContain("Get-Process -Name 'SmartGuard.Engine'");
  }
}
