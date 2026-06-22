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
}
