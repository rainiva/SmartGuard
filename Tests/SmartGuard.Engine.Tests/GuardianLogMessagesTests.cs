using FluentAssertions;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

public class GuardianLogMessagesTests
{
  [Fact]
  public void Heartbeat_without_final_target_facts_never_claims_normal_plan()
  {
    GuardianLogMessages.FormatHeartbeat("活跃", "平衡", 1, 90, true, true, -1)
      .Should().NotContain("计划正常").And.Contain("暂停");
    GuardianLogMessages.FormatStatusLabelChange("活跃", 1, "平衡", 90, true, -1)
      .Should().NotContain("计划正常");
  }

  [Fact]
  public void FormatHeartbeat_includes_brightness_when_supported()
  {
    GuardianLogMessages.FormatHeartbeat("活跃", "高性能", idleSeconds: 12, batteryPercent: 90, isOnAc: true, paused: false, brightness: 56)
      .Should().Be("运行情况：活跃，12秒未操作。当前电源计划：高性能。仅查看当前计划。电量90%，已接通电源，亮度56%。");
  }

  [Fact]
  public void FormatHeartbeat_shows_na_when_brightness_unsupported()
  {
    GuardianLogMessages.FormatHeartbeat("活跃", "平衡", idleSeconds: 900, batteryPercent: 50, isOnAc: false, paused: true, brightness: -1)
      .Should().Be("运行情况：活跃，900秒未操作。当前电源计划：平衡。守护已暂停。电量50%，使用电池，无法读取亮度。");
  }

  [Fact]
  public void FormatStatusLabelChange_includes_brightness()
  {
    GuardianLogMessages.FormatStatusLabelChange("空闲", idleSeconds: 420, "平衡", 80, isOnAc: true, brightness: 40)
      .Should().Be("使用状态变为：空闲，420秒未操作。当前电源计划：平衡。电量80%，已接通电源，亮度40%。");
  }

  [Fact]
  public void FormatBrightnessChange_shows_before_and_after()
  {
    GuardianLogMessages.FormatBrightnessChange(56, 40)
      .Should().Be("屏幕亮度从56%变为40%。");
  }
}
