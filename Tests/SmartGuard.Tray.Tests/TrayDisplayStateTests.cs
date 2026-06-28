using SmartGuard.Contracts;
using SmartGuard.Tray;

namespace SmartGuard.Tray.Tests;

public class TrayDisplayStateTests
{
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
