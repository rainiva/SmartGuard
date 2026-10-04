using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class LogViewStatusTextBuilderTests
{
    [Theory]
    [InlineData("degraded", "日志降级")]
    [InlineData("failed", "日志可能缺失")]
    [InlineData("unregistered", "日志状态未知")]
    public void Build_uses_shared_health_code_and_separate_freshness(string code, string expected)
    {
        var now = DateTime.Now;
        var snapshot = new LogViewSnapshot([], 0, false, "main.log", false, "", null, LogViewTimeRange.All);
        var method = typeof(LogViewStatusTextBuilder).GetMethod("Build")!;
        method.GetParameters().Should().HaveCount(6);
        var health = new SmartGuard.Contracts.LogHealthStatus(code, new DateTimeOffset(now), 1, 0, 0, 0, 0, 0);
        ((string)method.Invoke(null, [snapshot, now, null, false, null, health])!).Should().Contain(expected);
        var stale = health with { ObservedAtUtc = new DateTimeOffset(now).AddMinutes(-4) };
        ((string)method.Invoke(null, [snapshot, now, null, false, null, stale])!).Should().Contain("日志状态未知");
        var future = health with { ObservedAtUtc = new DateTimeOffset(now).AddMinutes(4) };
        ((string)method.Invoke(null, [snapshot, now, null, false, null, future])!).Should().Contain("日志状态未知");
    }

    [Fact]
    public void Build_uses_one_loaded_count_when_display_is_truncated()
    {
        var displayLines = new List<string>();
        for (var i = 0; i < 500; i++)
            displayLines.Add($"[INFO] 2026-06-21 10:00:00 line {i}");

        var snapshot = new LogViewSnapshot(
            displayLines,
            632,
            false,
            @"C:\SmartGuard\SmartGuard.log",
            false,
            "",
            null,
            LogViewTimeRange.All,
            632,
            true);

        var text = LogViewStatusTextBuilder.Build(snapshot, new DateTime(2026, 6, 21, 10, 2, 24));

        text.Should().Contain("显示 500 / 已加载 632 条");
        text.Should().NotContain("总计");
        text.Should().NotContain("渲染");
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
            .Should().Contain("空闲状态可能过期");
    }

    [Fact]
    public void Build_includes_older_history_hint_when_has_older()
    {
        var snapshot = new LogViewSnapshot(
            ["[INFO] 2026-06-21 10:00:00 heartbeat"],
            200,
            false,
            @"C:\SmartGuard\SmartGuard.log",
            false,
            "",
            null,
            LogViewTimeRange.All,
            1,
            false,
            null,
            HasOlderHistory: true);

        LogViewStatusTextBuilder.Build(snapshot, new DateTime(2026, 6, 21, 10, 2, 24))
            .Should().Contain("仍有更早记录");
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
