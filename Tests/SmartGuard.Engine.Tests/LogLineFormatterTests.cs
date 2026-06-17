using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class LogLineFormatterTests
{
  [Theory]
  [InlineData(LogLevel.Info, "[INFO]")]
  [InlineData(LogLevel.Warn, "[WARN]")]
  [InlineData(LogLevel.Error, "[ERROR]")]
  [InlineData(LogLevel.Heart, "[HEART]")]
  public void Format_includes_level_tag(LogLevel level, string tag)
  {
    var line = LogLineFormatter.Format(new DateTime(2026, 6, 16, 16, 0, 13), level, "test message");
    line.Should().Contain(tag);
    line.Should().EndWith("test message");
  }

  [Fact]
  public void Format_puts_level_tag_before_timestamp()
  {
    var line = LogLineFormatter.Format(new DateTime(2026, 6, 16, 16, 0, 13), LogLevel.Info, "x");
    line.Should().StartWith("[INFO] 2026-06-16 16:00:13 ");
  }

  [Fact]
  public void Format_does_not_duplicate_warn_prefix_in_message()
  {
    var line = LogLineFormatter.Format(
      new DateTime(2026, 6, 16, 16, 0, 13),
      LogLevel.Warn,
      "亮度写回未完全匹配，已重试 3 次");
    line.Should().Be("[WARN] 2026-06-16 16:00:13 亮度写回未完全匹配，已重试 3 次");
    line.Should().NotContain("WARN:");
  }
}
