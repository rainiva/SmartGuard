using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class PowerCfgParserTests
{
    [Fact]
    public void Parses_active_scheme_guid_from_powercfg_output()
    {
        const string output = """
        电源方案 GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (高性能)
        """;
        PowerCfgExecutor.ParseActiveSchemeGuid(output)
            .Should().Be(Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"));
    }

    [Fact]
    public void Parses_power_scheme_list_with_oem_plan_names()
    {
        const string output = """
        现有电源使用方案 (* Active)
        -----------------------------------
        电源方案 GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (平衡)
        电源方案 GUID: 56c8d9b8-d34d-4d19-96a4-c177cf5f4882  (自定义模式)
        电源方案 GUID: b8a2c9f4-7d3e-4a1b-9c2f-5e8d6a3b1c4f  (Honor Performance) *
        """;

        var catalog = PowerCfgExecutor.ParsePowerSchemeList(output);

        catalog.Should().HaveCount(3);
        catalog[Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e")].Should().Be("平衡");
        catalog[Guid.Parse("b8a2c9f4-7d3e-4a1b-9c2f-5e8d6a3b1c4f")].Should().Be("Honor Performance");
    }

    [Fact]
    public void Parses_display_name_from_getactivescheme_output()
    {
        const string output = """
        电源方案 GUID: b8a2c9f4-7d3e-4a1b-9c2f-5e8d6a3b1c4f  (Honor Performance)
        """;

        var info = PowerCfgExecutor.ParseCurrentPlanInfo(output);

        info.Should().NotBeNull();
        info!.Value.Guid.Should().Be(Guid.Parse("b8a2c9f4-7d3e-4a1b-9c2f-5e8d6a3b1c4f"));
        info!.Value.Name.Should().Be("Honor Performance");
    }
}
