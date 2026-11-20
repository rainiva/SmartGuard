using SmartGuard.Contracts;

namespace SmartGuard.Tray.Tests;

public class TrayGuardHealthDisplayTests
{
  private static readonly DateTimeOffset StalledSince = new(2026, 11, 20, 1, 0, 0, TimeSpan.Zero);

  [Fact]
  public void Apply_updates_when_only_guard_health_changes()
  {
    var state = new TrayDisplayState();
    state.Apply(Status(health: null)).Should().BeTrue();
    state.Apply(Status(Stalled())).Should().BeTrue("a stall alone must refresh the tray");
  }

  [Fact]
  public void Clone_preserves_guard_health_so_equal_statuses_stay_skipped()
  {
    var state = new TrayDisplayState();
    state.Apply(Status(Stalled())).Should().BeTrue();
    state.Apply(Status(Stalled())).Should().BeFalse("the stored clone must keep guardHealth for the equality check");
  }

  [Fact]
  public void Stall_replaces_the_probe_failure_clause_with_its_start_and_reason()
  {
    var line = TrayStatusFormatter.FormatStatusLine(Status(Stalled()));
    line.Should().Contain($"守护已停工（自 {StalledSince.ToLocalTime():HH:mm}，电源服务调用一直未返回）")
      .And.NotContain("状态探测失败");
    TrayStatusFormatter.FormatTooltip(Status(Stalled())).Should().Contain("守护已停工");
  }

  [Theory]
  [InlineData(GuardHealthCodes.FailureKinds.ApiError, "电源服务调用失败")]
  [InlineData(GuardHealthCodes.FailureKinds.TimedOut, "电源服务调用超时")]
  [InlineData("unregistered", "原因未知")]
  public void Stall_reason_codes_map_to_brief_text(string kind, string text)
    => TrayStatusFormatter.FormatStatusLine(Status(Stalled(kind))).Should().Contain(text);

  [Fact]
  public void Old_or_stale_status_never_shows_a_stall()
  {
    TrayStatusFormatter.FormatStatusLine(Status(health: null)).Should().Contain("状态探测失败").And.NotContain("停工");
    var stale = Status(Stalled());
    stale.timestamp = DateTimeOffset.UtcNow.AddHours(-1).ToString("O");
    TrayStatusFormatter.FormatStatusLine(stale).Should().NotContain("停工");
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(true, true)]
  public void Stall_notification_shows_regardless_of_plan_notification_preferences(bool onPlan, bool onExternal)
    => TrayNotificationPreferences.ShouldNotify(NotificationKinds.GuardStalled, onPlan, onExternal).Should().BeTrue();

  internal static GuardHealthStatus Stalled(string kind = GuardHealthCodes.FailureKinds.CallPending)
    => new(GuardHealthCodes.Stalled, StalledSince, kind);

  internal static StatusPayload Status(GuardHealthStatus? health, string reason = GuardStatusCodes.Reasons.ProbeFailed)
    => new()
    {
      timestamp = DateTimeOffset.UtcNow.ToString("O"),
      currentPlan = "平衡",
      batteryPercent = 50,
      brightness = 60,
      guardSource = GuardStatusCodes.Sources.None,
      guardReason = reason,
      logHealth = new LogHealthStatus(LogHealthCodes.Healthy, DateTimeOffset.UtcNow, 1, 0, 0, 0, 0, 0),
      guardHealth = health,
    };
}
