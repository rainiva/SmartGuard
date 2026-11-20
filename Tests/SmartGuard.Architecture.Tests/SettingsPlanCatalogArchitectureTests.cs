using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class SettingsPlanCatalogArchitectureTests
{
  [Fact]
  public void SettingsPolicyCoordinator_must_not_cache_plan_catalog_snapshot()
  {
    var policy = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsPolicyCoordinator.cs");
    policy.Should().NotMatchRegex(@"private\s+IReadOnlyDictionary<Guid,\s*string>\s+_planCatalog\b");
    policy.Should().Contain("SettingsPlanCatalogCoordinator");

    var catalog = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsPlanCatalogCoordinator.cs");
    catalog.Should().NotMatchRegex(@"private\s+IReadOnlyDictionary<Guid,\s*string>\s+_planCatalog\b");
    // S-05: the UI reads the IO-free snapshot; loads only run through the cancelable async path.
    catalog.Should().Contain("PowerPlanCatalogProvider.Snapshot");
    catalog.Should().NotContain("PowerPlanCatalogProvider.TryLoad()");
  }
}
