namespace SmartGuard.Configuration.Tests;

public class PowerPlanCatalogParserTests
{
  [Fact]
  public void ParseList_reads_guid_and_display_name()
  {
    const string output = """
    现有电源使用方案 (* Active)
    -----------------------------------
    电源方案 GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (平衡)
    电源方案 GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (高性能)
    """;

    var catalog = PowerPlanCatalogParser.ParseList(output);

    catalog.Should().HaveCount(2);
    catalog[Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e")].Should().Be("平衡");
  }
}
