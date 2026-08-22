using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class HeartbeatScheduleTests
{
  [Fact]
  public void Rejected_heartbeat_is_retried_on_next_iteration()
  {
    var time = new ManualTimeProvider();
    var schedule = new HeartbeatSchedule(time);

    schedule.IsDue(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(60)).Should().BeTrue();
    time.Advance(TimeSpan.FromSeconds(15));
    schedule.IsDue(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(60)).Should().BeTrue();
  }

  [Fact]
  public void Accepted_heartbeat_advances_monotonic_window()
  {
    var time = new ManualTimeProvider();
    var schedule = new HeartbeatSchedule(time);
    schedule.MarkAccepted();

    time.Advance(TimeSpan.FromMinutes(9));
    time.SetUtcNow(time.GetUtcNow().AddHours(-12));
    schedule.IsDue(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(60)).Should().BeFalse();

    time.AdvanceTimestamp(TimeSpan.FromMinutes(1));
    schedule.IsDue(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(60)).Should().BeTrue();
  }

  [Fact]
  public void Resume_requests_one_immediate_heartbeat_without_backfill()
  {
    var time = new ManualTimeProvider();
    var schedule = new HeartbeatSchedule(time);
    schedule.MarkAccepted();
    time.Advance(TimeSpan.FromHours(8));

    schedule.ResetAfterResume();

    schedule.IsDue(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(60)).Should().BeTrue();
  }
}
