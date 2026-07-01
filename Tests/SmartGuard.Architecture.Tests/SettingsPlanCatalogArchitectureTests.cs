using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class SettingsPlanCatalogArchitectureTests
{
  [Fact]
  public void SettingsPolicyCoordinator_must_not_cache_plan_catalog_snapshot()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsPolicyCoordinator.cs");
    source.Should().NotMatchRegex(@"private\s+IReadOnlyDictionary<Guid,\s*string>\s+_planCatalog\b");
    source.Should().NotContain("_planCatalog =");
    source.Should().Contain("PowerPlanCatalogProvider.TryLoad()");
  }
}
