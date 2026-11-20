using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public sealed class PowerSourceTransitionTrackerTests
{
  [Fact]
  public void First_observation_initializes_state_without_reporting_a_transition()
  {
    var tracker = new PowerSourceTransitionTracker();

    tracker.Observe(isOnAc: true).Should().BeFalse();
  }

  [Fact]
  public void Changed_power_source_is_reported_once_for_event_or_polling_observers()
  {
    var tracker = new PowerSourceTransitionTracker();
    tracker.Observe(isOnAc: true).Should().BeFalse();

    tracker.Observe(isOnAc: false).Should().BeTrue();
    tracker.Observe(isOnAc: false).Should().BeFalse();
    tracker.Observe(isOnAc: true).Should().BeTrue();
  }
}
