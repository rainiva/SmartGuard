using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class SettingsDisplayNameArchitectureTests
{
  [Fact]
  public void SettingsWindowController_must_not_hardcode_tier_display_names()
  {
    var policySource = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsPolicyCoordinator.cs");
    var catalogSource = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsPlanCatalogCoordinator.cs");
    var combined = policySource + catalogSource;
    combined.Should().NotContain($"\"{PowerPlanCatalogProvider.HighPerformanceDisplayName}\"",
      "use PowerPlanCatalogProvider.HighPerformanceDisplayName");
    combined.Should().NotContain($"\"{PowerPlanCatalogProvider.BalancedDisplayName}\"",
      "use PowerPlanCatalogProvider.BalancedDisplayName");
    combined.Should().NotContain($"\"{PowerPlanCatalogProvider.PowerSaverDisplayName}\"",
      "use PowerPlanCatalogProvider.PowerSaverDisplayName");
    combined.Should().Contain("PowerPlanCatalogProvider.HighPerformanceDisplayName");
    combined.Should().Contain("PowerPlanCatalogProvider.BalancedDisplayName");
    combined.Should().Contain("PowerPlanCatalogProvider.PowerSaverDisplayName");
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
