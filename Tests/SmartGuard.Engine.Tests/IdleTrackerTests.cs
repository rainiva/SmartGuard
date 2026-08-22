using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class IdleTrackerTests
{
    [Theory]
    [InlineData(0u, 100u, true)]
    [InlineData(1u, 100u, true)]
    [InlineData(2u, 100u, true)]
    [InlineData(50u, 100u, true)]
    [InlineData(94u, 100u, true)]
    [InlineData(95u, 100u, false)]
    [InlineData(120u, 100u, false)]
    [InlineData(uint.MaxValue, uint.MaxValue, false)]
    public void Detects_user_activity_when_api_idle_drops_or_is_near_zero(
        uint apiIdle,
        uint previousApiIdle,
        bool expected)
    {
        IdleTracker.IsUserActivity(apiIdle, previousApiIdle).Should().Be(expected);
    }

    [Fact]
    public void Seeds_awake_epoch_from_api_idle_on_first_sample()
    {
        var time = new ManualTimeProvider();
        var tracker = new IdleTracker(time);

        tracker.Sample(() => 900).Should().Be(900u);
        time.Advance(TimeSpan.FromSeconds(5));
        tracker.Sample(() => 905).Should().Be(905u);
    }

    [Fact]
    public void Uses_monotonic_elapsed_idle_when_api_idle_stalls()
    {
        var time = new ManualTimeProvider();
        var tracker = new IdleTracker(time);

        tracker.Sample(() => 480);
        time.Advance(TimeSpan.FromMinutes(48));
        tracker.Sample(() => 480)
            .Should().Be(3360u);
    }

    [Fact]
    public void Resets_wall_clock_when_user_activity_is_detected()
    {
        var time = new ManualTimeProvider();
        var tracker = new IdleTracker(time);

        tracker.Sample(() => 600);
        time.Advance(TimeSpan.FromMinutes(20));
        tracker.Sample(() => 620).Should().Be(1800u);
        time.Advance(TimeSpan.FromMinutes(1));
        tracker.Sample(() => 0).Should().Be(0u);
        time.Advance(TimeSpan.FromMinutes(2));
        tracker.Sample(() => 120).Should().Be(120u);
    }

    [Fact]
    public void Resume_reset_handles_api_idle_snapback_as_a_new_epoch()
    {
        var time = new ManualTimeProvider();
        var tracker = new IdleTracker(time);

        tracker.Sample(() => 480u);
        time.Advance(TimeSpan.FromMinutes(48));
        tracker.Sample(() => 480u).Should().Be(3360u);

        tracker.ResetAfterResume();
        tracker.Sample(() => 30u).Should().Be(0u);
    }

    [Fact]
    public void Resets_when_api_idle_drops_while_wall_idle_is_consistent()
    {
        var time = new ManualTimeProvider();
        var tracker = new IdleTracker(time);

        tracker.Sample(() => 100u);
        time.Advance(TimeSpan.FromSeconds(20));
        tracker.Sample(() => 120u).Should().Be(120u);
        time.Advance(TimeSpan.FromSeconds(10));
        tracker.Sample(() => 10u).Should().Be(10u);
    }

    [Fact]
    public void Monotonic_api_idle_reaches_balanced_threshold_without_reset()
    {
        var time = new ManualTimeProvider();
        var tracker = new IdleTracker(time);
        uint api = 0;
        uint lastIdle = 0;

        for (var i = 0; i < 25; i++)
        {
            api += 30;
            if (i > 0)
                time.Advance(TimeSpan.FromSeconds(30));
            var idle = tracker.Sample(() => api);
            idle.Should().BeGreaterThanOrEqualTo(lastIdle);
            lastIdle = idle;
        }

        lastIdle.Should().BeGreaterThanOrEqualTo(300u);
    }

    [Fact]
    public void Mouse_wake_sampled_eight_seconds_later_starts_a_new_awake_epoch()
    {
        var time = new ManualTimeProvider();
        var tracker = new IdleTracker(time);
        tracker.Sample(() => 295).Should().Be(295);
        time.Advance(TimeSpan.FromMinutes(1));
        tracker.ResetAfterResume();
        time.Advance(TimeSpan.FromSeconds(8));
        tracker.Sample(() => 8).Should().Be(0);
        time.Advance(TimeSpan.FromSeconds(15));
        tracker.Sample(() => 23).Should().BeInRange(15u, 23u);
    }

    [Fact]
    public void Awake_idle_uses_monotonic_elapsed_time_and_ignores_utc_clock_changes()
    {
        var time = new ManualTimeProvider();
        var tracker = new IdleTracker(time);
        tracker.Sample(() => 10).Should().Be(10);
        time.AdvanceTimestamp(TimeSpan.FromSeconds(30));
        time.SetUtcNow(time.GetUtcNow().AddHours(-8));
        tracker.Sample(() => 40).Should().Be(40);
    }
}
