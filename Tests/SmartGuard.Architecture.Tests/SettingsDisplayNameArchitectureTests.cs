using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class SettingsDisplayNameArchitectureTests
{
  [Fact]
  public void SettingsWindowController_must_not_hardcode_tier_display_names()
  {
    var policySource = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsPolicyCoordinator.cs");
    policySource.Should().NotContain($"\"{PowerPlanCatalogProvider.HighPerformanceDisplayName}\"",
      "use PowerPlanCatalogProvider.HighPerformanceDisplayName");
    policySource.Should().NotContain($"\"{PowerPlanCatalogProvider.BalancedDisplayName}\"",
      "use PowerPlanCatalogProvider.BalancedDisplayName");
    policySource.Should().NotContain($"\"{PowerPlanCatalogProvider.PowerSaverDisplayName}\"",
      "use PowerPlanCatalogProvider.PowerSaverDisplayName");
    policySource.Should().Contain("PowerPlanCatalogProvider.HighPerformanceDisplayName");
    policySource.Should().Contain("PowerPlanCatalogProvider.BalancedDisplayName");
    policySource.Should().Contain("PowerPlanCatalogProvider.PowerSaverDisplayName");
  }

  [Fact]
  public void PowerPlanMappingValidator_must_not_hardcode_tier_display_names()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/PowerPlanMappingValidator.cs");
    source.Should().NotContain($"\"{PowerPlanCatalogProvider.HighPerformanceDisplayName}\"");
    source.Should().NotContain($"\"{PowerPlanCatalogProvider.BalancedDisplayName}\"");
    source.Should().NotContain($"\"{PowerPlanCatalogProvider.PowerSaverDisplayName}\"");
    source.Should().Contain("PowerPlanCatalogProvider.HighPerformanceDisplayName");
  }
}
