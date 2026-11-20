using SmartGuard.Contracts;
using SmartGuard.Tray;

namespace SmartGuard.Tray.Tests;

public class TrayDisplayStateTests
{
    [Fact]
    public void Health_change_and_expiry_refresh_text_without_repeating_stable_samples()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new TrayDisplayState(() => now);
        var status = new StatusPayload { timestamp = now.ToString("O"), currentPlan = "平衡",
            guardSource = GuardStatusCodes.Sources.Automatic, guardReason = GuardStatusCodes.Reasons.ForegroundMatched,
            logHealth = new LogHealthStatus(LogHealthCodes.Healthy, now, 1, 0, 0, 0, 0, 0) };
        state.Apply(status).Should().BeTrue();
        status.logHealth = status.logHealth with { Code = LogHealthCodes.Degraded };
        state.Apply(status).Should().BeTrue();
        state.StatusLine.Should().Contain("日志降级").And.Contain("守护中");
        status.logHealth = status.logHealth with { ObservedAtUtc = now.AddSeconds(1), PrimaryWrittenLines = 2 };
        state.Apply(status).Should().BeFalse("计数/时间不改变短提示，不重复播报");
        now = now.AddMinutes(4);
        state.Apply(status).Should().BeTrue();
        state.StatusLine.Should().Contain("日志状态未知");
    }

    [Fact]
    public void Apply_updates_text_once_for_same_status()
    {
        var state = new TrayDisplayState();
        var status = new StatusPayload
        {
            currentPlan = "平衡",
            batteryPercent = 88,
            isOnAC = true,
            brightness = 60,
            paused = false,
        };

        state.Apply(status).Should().BeTrue();
        state.StatusLine.Should().Contain("平衡");
        state.Tooltip.Should().Contain("88%");

        state.Apply(status).Should().BeFalse();
    }

    [Fact]
    public void Apply_updates_waiting_text_when_status_is_missing()
    {
        var state = new TrayDisplayState();
        state.Apply(null).Should().BeTrue();
        state.StatusLine.Should().Contain("等待核心服务");
    }

    [Fact]
    public void Apply_updates_when_plan_changes()
    {
        var state = new TrayDisplayState();
        state.Apply(new StatusPayload { currentPlan = "平衡", batteryPercent = 50, isOnAC = true, brightness = 40 })
            .Should().BeTrue();
        state.Apply(new StatusPayload { currentPlan = "节能", batteryPercent = 50, isOnAC = true, brightness = 40 })
            .Should().BeTrue();
        state.StatusLine.Should().Contain("节能");
    }
}
