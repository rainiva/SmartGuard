using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class LogViewStatusTextBuilderTests
{
    [Fact]
    public void Build_includes_match_count_when_search_is_active()
    {
        var snapshot = new LogViewSnapshot(
            ["[INFO] 2026-06-21 10:00:00 brightness changed"],
            10,
            false,
            @"C:\SmartGuard\SmartGuard.log",
            false,
            "brightness",
            null);

        LogViewStatusTextBuilder.Build(snapshot, new DateTime(2026, 6, 21, 10, 2, 24))
            .Should().Contain("匹配 1 条");
    }

    [Fact]
    public void Build_omits_match_count_when_search_is_empty()
    {
        var snapshot = new LogViewSnapshot(
            ["[INFO] 2026-06-21 10:00:00 brightness changed"],
            10,
            false,
            @"C:\SmartGuard\SmartGuard.log",
            false,
            "",
            null);

        LogViewStatusTextBuilder.Build(snapshot, new DateTime(2026, 6, 21, 10, 2, 24))
            .Should().NotContain("匹配");
    }
}
