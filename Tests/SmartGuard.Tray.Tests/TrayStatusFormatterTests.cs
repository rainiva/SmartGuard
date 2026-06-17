using SmartGuard.Contracts;

namespace SmartGuard.Tray.Tests;

public class TrayStatusFormatterTests
{
  [Fact]
  public void FormatTooltip_includes_plan_battery_and_brightness()
  {
    var status = new StatusPayload
    {
      currentPlan = "高性能",
      batteryPercent = 90,
      isOnAC = true,
      brightness = 56,
      paused = false,
    };

    TrayStatusFormatter.FormatTooltip(status)
      .Should().Be("计划: 高性能 | 90% 插电 | 亮度56%");
  }

  [Fact]
  public void FormatTooltip_shows_waiting_when_status_null()
  {
    TrayStatusFormatter.FormatTooltip(null)
      .Should().Be("智能电源守护（等待核心服务）");
  }

  [Fact]
  public void FormatStatusLine_includes_pause_marker()
  {
    var status = new StatusPayload
    {
      currentPlan = "平衡",
      batteryPercent = 50,
      isOnAC = false,
      paused = true,
    };

    TrayStatusFormatter.FormatStatusLine(status)
      .Should().Be("计划：平衡 | 50% 电池 | 已暂停");
  }
}
