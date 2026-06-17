using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class PowerEventFormatterTests
{
  [Theory]
  [InlineData(true, "插电")]
  [InlineData(false, "电池")]
  public void FormatMessage_describes_power_source(bool isOnAc, string keyword)
  {
    PowerEventFormatter.FormatMessage(isOnAc).Should().Contain(keyword);
    PowerEventFormatter.FormatMessage(isOnAc).Should().Contain("立即重新评估");
  }
}
