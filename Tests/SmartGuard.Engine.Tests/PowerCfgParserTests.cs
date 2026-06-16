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
}
