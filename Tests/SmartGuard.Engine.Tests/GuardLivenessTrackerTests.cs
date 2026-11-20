using SmartGuard.Contracts;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

public class GuardLivenessTrackerTests
{
  private static readonly TimeSpan Round = TimeSpan.FromSeconds(15);

  private static (ManualTimeProvider Clock, GuardLivenessTracker Tracker) Create()
  {
    var clock = new ManualTimeProvider();
    return (clock, new GuardLivenessTracker(clock));
  }

  // One failing, applicable round per call; returns the transition of the last round.
  private static GuardLivenessObservation FailFor(ManualTimeProvider clock, GuardLivenessTracker tracker, TimeSpan span,
    string kind = GuardHealthCodes.FailureKinds.CallPending)
  {
    var last = tracker.Observe(true, false, kind, false);
    for (var elapsed = TimeSpan.Zero; elapsed < span; elapsed += Round)
    {
      clock.Advance(Round);
      last = tracker.Observe(true, false, kind, false);
    }
    return last;
  }

  [Fact]
  public void Failing_reads_stay_healthy_below_the_threshold_and_stall_at_it_exactly_once()
  {
    var (clock, tracker) = Create();
    FailFor(clock, tracker, GuardLivenessTracker.Threshold - Round);
    tracker.Current.Code.Should().Be(GuardHealthCodes.Healthy);

    clock.Advance(Round);
    tracker.Observe(true, false, GuardHealthCodes.FailureKinds.CallPending, false)
      .Transition.Should().Be(GuardLivenessTransition.Entered);
    tracker.Current.Should().Be(new GuardHealthStatus(GuardHealthCodes.Stalled,
      new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero), GuardHealthCodes.FailureKinds.CallPending));

    clock.Advance(Round);
    tracker.Observe(true, false, GuardHealthCodes.FailureKinds.CallPending, false)
      .Transition.Should().Be(GuardLivenessTransition.None, "a stall is entered once");
    tracker.Current.Code.Should().Be(GuardHealthCodes.Stalled);
  }

  [Fact]
  public void One_success_restarts_the_window()
  {
    var (clock, tracker) = Create();
    FailFor(clock, tracker, TimeSpan.FromMinutes(9));
    clock.Advance(Round);
    tracker.Observe(true, true, null, false);
    FailFor(clock, tracker, TimeSpan.FromMinutes(9));
    tracker.Current.Code.Should().Be(GuardHealthCodes.Healthy);
  }

  [Fact]
  public void Not_applicable_rounds_publish_not_applicable_and_restart_the_window()
  {
    var (clock, tracker) = Create();
    FailFor(clock, tracker, TimeSpan.FromMinutes(9));
    clock.Advance(TimeSpan.FromHours(1));
    tracker.Observe(false, false, GuardHealthCodes.FailureKinds.CallPending, false);
    tracker.Current.Code.Should().Be(GuardHealthCodes.NotApplicable);

    FailFor(clock, tracker, TimeSpan.FromMinutes(9));
    tracker.Current.Code.Should().Be(GuardHealthCodes.Healthy, "time while guarding was not expected never counts");
  }

  [Fact]
  public void Resume_restarts_a_window_that_has_not_stalled()
  {
    var (clock, tracker) = Create();
    FailFor(clock, tracker, TimeSpan.FromMinutes(9));
    clock.Advance(TimeSpan.FromHours(8));   // the monotonic clock may or may not count sleep
    tracker.Observe(true, false, GuardHealthCodes.FailureKinds.CallPending, resumed: true);
    tracker.Current.Code.Should().Be(GuardHealthCodes.Healthy);
    FailFor(clock, tracker, TimeSpan.FromMinutes(9));
    tracker.Current.Code.Should().Be(GuardHealthCodes.Healthy);
  }

  [Fact]
  public void A_stall_survives_resume_and_pauses_and_ends_only_with_a_successful_read()
  {
    var (clock, tracker) = Create();
    FailFor(clock, tracker, GuardLivenessTracker.Threshold);
    tracker.Current.Code.Should().Be(GuardHealthCodes.Stalled);

    clock.Advance(Round);
    tracker.Observe(true, false, GuardHealthCodes.FailureKinds.ApiError, resumed: true);
    tracker.Current.Code.Should().Be(GuardHealthCodes.Stalled, "recovery is never inferred from time or a wake");
    tracker.Current.LastFailureKind.Should().Be(GuardHealthCodes.FailureKinds.ApiError);
    clock.Advance(Round);
    tracker.Observe(false, false, GuardHealthCodes.FailureKinds.ApiError, false);
    tracker.Current.Code.Should().Be(GuardHealthCodes.NotApplicable);
    clock.Advance(Round);
    tracker.Observe(true, false, GuardHealthCodes.FailureKinds.ApiError, false);
    tracker.Current.Code.Should().Be(GuardHealthCodes.Stalled);

    clock.Advance(Round);
    var recovered = tracker.Observe(false, true, null, false);   // a read in a paused round still counts
    recovered.Transition.Should().Be(GuardLivenessTransition.Recovered);
    recovered.Duration.Should().Be(GuardLivenessTracker.Threshold + 4 * Round);
    tracker.Current.Code.Should().Be(GuardHealthCodes.NotApplicable);
    clock.Advance(Round);
    tracker.Observe(true, true, null, false).Transition.Should().Be(GuardLivenessTransition.None);
    tracker.Current.Should().Be(new GuardHealthStatus(GuardHealthCodes.Healthy, null, null));
  }

  // PowerPlanFailureKind is internal and xUnit only runs public methods, so the kind travels as an int.
  [Theory]
  [InlineData((int)PowerPlanFailureKind.ApiFailed, GuardHealthCodes.FailureKinds.ApiError)]
  [InlineData((int)PowerPlanFailureKind.CallPending, GuardHealthCodes.FailureKinds.CallPending)]
  [InlineData((int)PowerPlanFailureKind.TimedOut, GuardHealthCodes.FailureKinds.TimedOut)]
  [InlineData((int)PowerPlanFailureKind.NotAdmitted, GuardHealthCodes.FailureKinds.Other)]
  public void Failure_kinds_map_to_stable_codes(int kind, string code)
    => GuardLivenessTracker.FailureKindCode((PowerPlanFailureKind)kind).Should().Be(code);
}
