using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

[Collection("PowerPlanCatalogProvider")]
[Trait("Category", "Live")]
public class LiveCatalogProbeTests
{
  [Fact]
  public async Task LoadWithRetryAsync_reads_local_power_plans()
  {
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    var catalog = await PowerPlanCatalogProvider.LoadWithRetryAsync();
    catalog.Should().NotBeEmpty();
    catalog.Should().ContainKey(PowerPlanCatalogProvider.HighPerformancePlanGuid);
    catalog.Should().ContainKey(PowerPlanCatalogProvider.BalancedPlanGuid);
    catalog.Should().ContainKey(PowerPlanCatalogProvider.PowerSaverPlanGuid);
    catalog[PowerPlanCatalogProvider.PowerSaverPlanGuid].Should().Be("节能");
  }
}
