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
    public void Detects_user_activity_when_api_idle_drops_or_is_near_zero(
        uint apiIdle,
        uint previousApiIdle,
        bool expected)
    {
        IdleTracker.IsUserActivity(apiIdle, previousApiIdle).Should().Be(expected);
    }

    [Fact]
    public void Seeds_wall_clock_from_api_idle_on_first_sample()
    {
        var utcNow = new DateTime(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc);
        var tracker = new IdleTracker();

        tracker.Sample(() => 900, utcNow).Should().Be(900u);
        tracker.Sample(() => 905, utcNow.AddSeconds(5)).Should().Be(905u);
    }

    [Fact]
    public void Uses_wall_clock_idle_when_api_idle_stalls_across_sleep()
    {
        var start = new DateTime(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc);
        var tracker = new IdleTracker();

        tracker.Sample(() => 480, start);
        tracker.Sample(() => 480, start.AddMinutes(48))
            .Should().Be(3360u);
    }

    [Fact]
    public void Resets_wall_clock_when_user_activity_is_detected()
    {
        var start = new DateTime(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc);
        var tracker = new IdleTracker();

        tracker.Sample(() => 600, start);
        tracker.Sample(() => 620, start.AddMinutes(20)).Should().Be(1800u);
        tracker.Sample(() => 0, start.AddMinutes(21)).Should().Be(0u);
        tracker.Sample(() => 120, start.AddMinutes(23)).Should().Be(120u);
    }

    [Fact]
    public void Does_not_reset_wall_idle_when_api_drops_after_sleep_stall()
    {
        var start = new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);
        var tracker = new IdleTracker();

        tracker.Sample(() => 480u, start);
        tracker.Sample(() => 480u, start.AddMinutes(48)).Should().Be(3360u);

        // Wake: GetLastInputInfo often snaps back without real user input.
        tracker.Sample(() => 30u, start.AddMinutes(48).AddSeconds(30))
            .Should().BeGreaterThanOrEqualTo(3390u);
    }

    [Fact]
    public void Resets_when_api_idle_drops_while_wall_idle_is_consistent()
    {
        var start = new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);
        var tracker = new IdleTracker();

        tracker.Sample(() => 100u, start);
        tracker.Sample(() => 120u, start.AddSeconds(20)).Should().Be(120u);
        tracker.Sample(() => 10u, start.AddSeconds(30)).Should().Be(10u);
    }

    [Theory]
    [InlineData(0u, 100u, 100u, true)]
    [InlineData(30u, 480u, 3360u, false)]
    [InlineData(10u, 120u, 130u, true)]
    [InlineData(50u, 100u, 100u, true)]
    public void ShouldResetForActivity_distinguishes_real_input_from_sleep_recovery(
        uint apiIdle,
        uint previousApiIdle,
        uint wallIdle,
        bool expected)
    {
        IdleTracker.ShouldResetForActivity(apiIdle, previousApiIdle, wallIdle).Should().Be(expected);
    }

    [Fact]
    public void Monotonic_api_idle_reaches_balanced_threshold_without_reset()
    {
        var start = new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);
        var tracker = new IdleTracker();
        uint api = 0;
        uint lastIdle = 0;

        for (var i = 0; i < 25; i++)
        {
            api += 30;
            var sampleAt = start.AddSeconds(i * 30);
            var idle = tracker.Sample(() => api, sampleAt);
            idle.Should().BeGreaterThanOrEqualTo(lastIdle);
            lastIdle = idle;
        }

        lastIdle.Should().BeGreaterThanOrEqualTo(300u);
    }
}
