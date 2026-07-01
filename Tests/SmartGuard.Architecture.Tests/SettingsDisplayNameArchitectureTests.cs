using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class SettingsDisplayNameArchitectureTests
{
  [Fact]
  public void SettingsWindowController_must_not_hardcode_tier_display_names()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsWindowController.cs");
    source.Should().NotContain($"\"{PowerPlanCatalogProvider.HighPerformanceDisplayName}\"",
      "use PowerPlanCatalogProvider.HighPerformanceDisplayName");
    source.Should().NotContain($"\"{PowerPlanCatalogProvider.BalancedDisplayName}\"",
      "use PowerPlanCatalogProvider.BalancedDisplayName");
    source.Should().NotContain($"\"{PowerPlanCatalogProvider.PowerSaverDisplayName}\"",
      "use PowerPlanCatalogProvider.PowerSaverDisplayName");
    source.Should().Contain("PowerPlanCatalogProvider.HighPerformanceDisplayName");
    source.Should().Contain("PowerPlanCatalogProvider.BalancedDisplayName");
    source.Should().Contain("PowerPlanCatalogProvider.PowerSaverDisplayName");
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
