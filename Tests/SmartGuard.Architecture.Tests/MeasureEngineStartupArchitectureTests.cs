using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class MeasureEngineStartupArchitectureTests
{
  [Fact]
  public void Measure_EngineStartup_must_use_shared_log_path_constants()
  {
    var content = SourceScanHelper.ReadSource("scripts/Measure-EngineStartup.ps1");
    content.Should().Contain("SmartGuardPathConstants.ps1");
    content.Should().Contain("$SmartGuardDefaultLogFileName");
    content.Should().NotContain("'SmartGuard.log'");
  }

  [Fact]
  public void Measure_EngineStartup_allows_benchmark_only_start_with_marker()
  {
    var content = SourceScanHelper.ReadSource("scripts/Measure-EngineStartup.ps1");
    content.Should().Contain("# benchmark-only-start");
    content.Should().Contain("Start-Process");
    content.Should().Contain("Stop-SmartGuardProcesses");
  }
}
