using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LiveCatalogProbeTests
{
  [Fact]
  public void TryLoad_reads_local_power_plans()
  {
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    var catalog = PowerPlanCatalogProvider.TryLoad();
    catalog.Should().NotBeEmpty();
    catalog.Should().ContainKey(PowerPlanCatalogProvider.HighPerformancePlanGuid);
    catalog.Should().ContainKey(PowerPlanCatalogProvider.BalancedPlanGuid);
  }
}
