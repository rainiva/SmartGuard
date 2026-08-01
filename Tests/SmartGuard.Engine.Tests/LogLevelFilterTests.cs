using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class LogLevelFilterTests
{
  [Theory]
  [InlineData(LogLevel.Trace, LogLevel.Info, false)]
  [InlineData(LogLevel.Debug, LogLevel.Info, false)]
  [InlineData(LogLevel.Info, LogLevel.Info, true)]
  [InlineData(LogLevel.Warn, LogLevel.Info, true)]
  [InlineData(LogLevel.Error, LogLevel.Warn, true)]
  [InlineData(LogLevel.Critical, LogLevel.Error, true)]
  [InlineData(LogLevel.Info, LogLevel.Warn, false)]
  public void ShouldWrite_compares_level_against_minimum(LogLevel level, LogLevel minimum, bool expected)
  {
    LogLevelFilter.ShouldWrite(level, minimum).Should().Be(expected);
  }

  [Fact]
  public void ShouldWrite_always_passes_heart_regardless_of_minimum()
  {
    LogLevelFilter.ShouldWrite(LogLevel.Heart, LogLevel.Critical).Should().BeTrue(
      "Heart 不参与级别过滤，频率由 Slice 5 的采样治理控制");
  }

  [Theory]
  [InlineData("Debug", LogLevel.Debug)]
  [InlineData("warn", LogLevel.Warn)]
  [InlineData("CRITICAL", LogLevel.Critical)]
  [InlineData("Trace", LogLevel.Trace)]
  public void ParseMinimum_parses_case_insensitively(string text, LogLevel expected)
  {
    LogLevelFilter.ParseMinimum(text).Should().Be(expected);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("verbose")]
  [InlineData("Heart")]
  public void ParseMinimum_falls_back_to_info_for_invalid_or_heart(string? text)
  {
    LogLevelFilter.ParseMinimum(text).Should().Be(LogLevel.Info);
  }
}
