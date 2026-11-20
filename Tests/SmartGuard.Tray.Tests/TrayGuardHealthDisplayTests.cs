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
