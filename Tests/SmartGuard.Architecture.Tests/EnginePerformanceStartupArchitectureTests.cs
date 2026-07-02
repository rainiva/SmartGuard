using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class EnginePerformanceStartupArchitectureTests
{
  [Fact]
  public void Engine_startup_performance_test_must_use_5000ms_budget_and_settle_after_stop()
  {
    var source = SourceScanHelper.ReadSource(
      "Tests/SmartGuard.Engine.PerformanceTests/EnginePerformanceTests.cs");

    source.Should().Contain("BeLessThan(5000)");
    source.Should().MatchRegex(@"StopEngine\(\)[\s\S]*Thread\.Sleep");
  }
}
