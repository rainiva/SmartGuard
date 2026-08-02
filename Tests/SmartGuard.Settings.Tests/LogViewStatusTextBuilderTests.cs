using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class LogViewStatusTextBuilderTests
{
    [Fact]
    public void Build_shows_real_disk_total_even_when_display_is_truncated()
    {
        // 锁定行为：显示层截断为 500 行时，「总计」仍呈现磁盘真实总行数（两段式）
        var displayLines = new List<string>();
        for (var i = 0; i < 500; i++)
            displayLines.Add($"[INFO] 2026-06-21 10:00:00 line {i}");

        var snapshot = new LogViewSnapshot(
            displayLines,
            15000,
            false,
            @"C:\SmartGuard\SmartGuard.log",
            false,
            "",
            null,
            LogViewTimeRange.All,
            15000,
            true);

        var text = LogViewStatusTextBuilder.Build(snapshot, new DateTime(2026, 6, 21, 10, 2, 24));

        text.Should().Contain("总计 15000 行");
        text.Should().Contain("显示 500");
    }

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

    [Fact]
    public void Build_includes_idle_seconds_when_provided()
    {
        var snapshot = new LogViewSnapshot(
            ["[INFO] 2026-06-21 10:00:00 heartbeat"],
            1,
            false,
            @"C:\SmartGuard\SmartGuard.log",
            false,
            "",
            null);

        LogViewStatusTextBuilder.Build(snapshot, new DateTime(2026, 6, 21, 10, 2, 24), idleSeconds: 480)
            .Should().Contain("当前空闲 480 秒");
    }

    [Fact]
    public void Build_includes_stale_hint_when_status_may_be_stale()
    {
        var snapshot = new LogViewSnapshot(
            ["[INFO] 2026-06-21 10:00:00 heartbeat"],
            1,
            false,
            @"C:\SmartGuard\SmartGuard.log",
            false,
            "",
            null);

        LogViewStatusTextBuilder.Build(
                snapshot,
                new DateTime(2026, 6, 21, 10, 2, 24),
                idleSeconds: 12,
                statusMayBeStale: true)
            .Should().Contain("状态可能过期");
    }

    [Fact]
    public void Build_omits_idle_seconds_when_not_provided()
    {
        var snapshot = new LogViewSnapshot(
            ["[INFO] 2026-06-21 10:00:00 heartbeat"],
            1,
            false,
            @"C:\SmartGuard\SmartGuard.log",
            false,
            "",
            null);

        LogViewStatusTextBuilder.Build(snapshot, new DateTime(2026, 6, 21, 10, 2, 24))
            .Should().NotContain("当前空闲");
    }
}
