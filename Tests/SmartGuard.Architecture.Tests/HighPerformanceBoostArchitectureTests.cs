using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class HighPerformanceBoostArchitectureTests
{
  [Fact]
  public void HighPerformanceBoost_must_not_call_repository_SetManualHighPerformanceUntil_directly()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/HighPerformanceBoost.cs");
    source.Should().NotContain(
      "repository.SetManualHighPerformanceUntil",
      "manual HP writes must go through ConfigMutationService");
    source.Should().Contain("ConfigMutationService");
  }
}
