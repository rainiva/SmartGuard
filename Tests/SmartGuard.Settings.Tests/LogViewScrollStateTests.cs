using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class LogViewScrollStateTests
{
    [Theory]
    [InlineData(1000, 960, true)]
    [InlineData(1000, 950, false)]
    [InlineData(0, 0, true)]
    public void IsNearTail_matches_threshold(int scrollableHeight, double verticalOffset, bool expected)
    {
        LogViewScrollState.IsNearTail(scrollableHeight, verticalOffset).Should().Be(expected);
    }
}
