using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class LogLineTimestampParserTests
{
    [Fact]
    public void TryParseTimestamp_reads_timestamp_from_tagged_line()
    {
        LogLineTimestampParser.TryParseTimestamp(
                "[INFO] 2026-06-21 10:15:30 brightness changed",
                out var timestamp)
            .Should().BeTrue();

        timestamp.Should().Be(new DateTime(2026, 6, 21, 10, 15, 30));
    }

    [Fact]
    public void TryParseTimestamp_returns_false_for_raw_line()
    {
        LogLineTimestampParser.TryParseTimestamp("[RAW] plain line", out _)
            .Should().BeFalse();
    }
}
