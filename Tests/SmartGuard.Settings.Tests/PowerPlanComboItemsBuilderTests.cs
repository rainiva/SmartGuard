namespace SmartGuard.Settings.Tests;

public class PowerPlanComboItemsBuilderTests
{
  [Fact]
  public void Build_includes_orphan_guid_when_not_in_catalog()
  {
    var orphan = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    var catalog = new Dictionary<Guid, string>
    {
      [Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e")] = "平衡",
    };

    var items = PowerPlanComboItemsBuilder.Build(catalog, orphan, "节能");

    items.Should().ContainSingle(i => i.PlanGuid == orphan);
    items.Single(i => i.PlanGuid == orphan).DisplayName.Should().Be("节能（未在本机找到）");
  }

  [Theory]
  [InlineData("Loading", "高性能（正在加载）")]
  [InlineData("Failed", "高性能（无法读取计划列表）")]
  [InlineData("Listed", "高性能（名称读取失败）")]
  [InlineData("Absent", "高性能（未在本机找到）")]
  public void Selected_plan_label_tells_loading_unavailable_unreadable_and_absent_apart(string situation, string expected)
  {
    var selected = Guid.Parse("56c8d9b8-d34d-4d19-96a4-c177cf5f4882");
    var none = new Dictionary<Guid, string>();
    var snapshot = situation switch
    {
      "Loading" => SmartGuard.Configuration.PowerPlanCatalogSnapshot.Loading,
      "Failed" => SmartGuard.Configuration.PowerPlanCatalogSnapshot.Failed,
      "Listed" => new SmartGuard.Configuration.PowerPlanCatalogSnapshot(SmartGuard.Configuration.PowerPlanCatalogState.Loaded,
        none, new HashSet<Guid> { selected }, new HashSet<Guid>()),
      _ => new SmartGuard.Configuration.PowerPlanCatalogSnapshot(SmartGuard.Configuration.PowerPlanCatalogState.Loaded,
        none, new HashSet<Guid>(), new HashSet<Guid>()),
    };
    PowerPlanComboItemsBuilder.Build(snapshot, selected, "高性能").Should().ContainSingle()
      .Which.DisplayName.Should().Be(expected);
  }
}
