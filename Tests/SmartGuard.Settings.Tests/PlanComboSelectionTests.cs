using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class PlanComboSelectionTests
{
  [Fact]
  public void ResolveSelectedGuid_uses_config_guid_when_combo_has_no_selection()
  {
    var configGuid = PowerPlanCatalogProvider.BalancedPlanGuid;
    PlanComboSelection.ResolveSelectedGuid(Guid.Empty, configGuid).Should().Be(configGuid);
  }

  [Fact]
  public void FindItem_returns_catalog_entry_for_config_guid()
  {
    var selected = PowerPlanCatalogProvider.HighPerformancePlanGuid;
    var catalog = new Dictionary<Guid, string>
    {
      [selected] = "高性能",
      [PowerPlanCatalogProvider.BalancedPlanGuid] = "平衡",
    };
    var items = PowerPlanComboItemsBuilder.Build(catalog, selected, "高性能");

    PlanComboSelection.FindItem(items, selected)?.DisplayName.Should().Be("高性能");
  }
}
