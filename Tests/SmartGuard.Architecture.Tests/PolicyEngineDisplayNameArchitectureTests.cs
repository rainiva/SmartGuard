using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class PolicyEngineDisplayNameArchitectureTests
{
  [Fact]
  public void PolicyEngine_does_not_hardcode_tier_display_names()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Domain/PolicyEngine.cs");
    source.Should().NotContain($"\"{PowerPlanCatalogProvider.HighPerformanceDisplayName}\"");
    source.Should().NotContain($"\"{PowerPlanCatalogProvider.BalancedDisplayName}\"");
    source.Should().NotContain($"\"{PowerPlanCatalogProvider.PowerSaverDisplayName}\"");
    source.Should().Contain(nameof(PowerPlanCatalogProvider));
  }
}
