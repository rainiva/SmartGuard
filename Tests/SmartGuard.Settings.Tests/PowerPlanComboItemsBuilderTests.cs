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
}
