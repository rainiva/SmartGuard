using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class LogViewTimeRangeFilterTests
{
    private static readonly DateTime Now = new(2026, 6, 21, 10, 30, 0);

    [Fact]
    public void IsWithinRange_allows_all_when_mode_is_all()
    {
        var timestamp = new DateTime(2020, 1, 1, 0, 0, 0);

        LogViewTimeRangeFilter.IsWithinRange(
                LogViewTimeRange.All,
                timestamp,
                Now,
                null,
                null)
            .Should().BeTrue();
    }

    [Fact]
    public void IsWithinRange_limits_to_today()
    {
        var today = new DateTime(2026, 6, 21, 9, 0, 0);
        var yesterday = new DateTime(2026, 6, 20, 23, 59, 59);

        LogViewTimeRangeFilter.IsWithinRange(LogViewTimeRange.Today, today, Now, null, null)
            .Should().BeTrue();
        LogViewTimeRangeFilter.IsWithinRange(LogViewTimeRange.Today, yesterday, Now, null, null)
            .Should().BeFalse();
    }

    [Fact]
    public void IsWithinRange_limits_to_last_hour()
    {
        var recent = Now.AddMinutes(-30);
        var old = Now.AddHours(-2);

        LogViewTimeRangeFilter.IsWithinRange(LogViewTimeRange.LastHour, recent, Now, null, null)
            .Should().BeTrue();
        LogViewTimeRangeFilter.IsWithinRange(LogViewTimeRange.LastHour, old, Now, null, null)
            .Should().BeFalse();
    }

    [Fact]
    public void IsWithinRange_limits_to_custom_range()
    {
        var start = new DateTime(2026, 6, 21, 10, 0, 0);
        var end = new DateTime(2026, 6, 21, 11, 0, 0);

        LogViewTimeRangeFilter.IsWithinRange(
                LogViewTimeRange.Custom,
                new DateTime(2026, 6, 21, 10, 30, 0),
                Now,
                start,
                end)
            .Should().BeTrue();

        LogViewTimeRangeFilter.IsWithinRange(
                LogViewTimeRange.Custom,
                new DateTime(2026, 6, 21, 9, 30, 0),
                Now,
                start,
                end)
            .Should().BeFalse();
    }
}
