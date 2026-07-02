using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class EnginePerformanceStopArchitectureTests
{
  [Fact]
  public void EnginePerformanceTests_must_not_kill_engine_by_process_name()
  {
    var source = SourceScanHelper.ReadSource("Tests/SmartGuard.Engine.PerformanceTests/EnginePerformanceTests.cs");
    source.Should().NotContain("GetProcessesByName");
    source.Should().NotContain(".Kill(");
    source.Should().Contain("PerformanceTestEngineLifecycle");
  }

  [Fact]
  public void PerformanceTestEngineLifecycle_must_use_StopProcesses_not_uninstall()
  {
    var source = SourceScanHelper.ReadSource(
      "Tests/SmartGuard.Engine.PerformanceTests/PerformanceTestEngineLifecycle.cs");
    source.Should().Contain("EngineLifecycle.StopProcesses()");
    source.Should().NotContain("--uninstall");
  }
}
