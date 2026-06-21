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

  [Fact]
  public void TryParseQueryHeader_reads_scheme_name_from_powercfg_query()
  {
    const string output = """
    电源方案 GUID: a1841308-3541-4fab-bc81-f71556f20b4a  (节能)
      GUID 别名: SCHEME_MAX
    """;

    PowerPlanCatalogParser.TryParseQueryHeader(output, out var guid, out var name).Should().BeTrue();
    guid.Should().Be(Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a"));
    name.Should().Be("节能");
  }

  [Fact]
  public void EnrichWithHiddenSchemes_adds_power_saver_missing_from_list_output()
  {
    var catalog = PowerPlanCatalogParser.ParseList("""
      电源方案 GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (平衡)
      电源方案 GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (高性能)
      """);

    PowerPlanCatalogProvider.EnrichWithHiddenSchemes(
      catalog,
      guid => guid == Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a") ? "节能" : null);

    catalog.Should().ContainKey(Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a"));
    catalog[Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a")].Should().Be("节能");
  }
}
