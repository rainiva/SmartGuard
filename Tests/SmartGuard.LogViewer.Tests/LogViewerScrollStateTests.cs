namespace SmartGuard.LogViewer.Tests;

public class LogViewerScrollStateTests
{
  [Theory]
  [InlineData(1000, 950, true)]
  [InlineData(1000, 800, false)]
  [InlineData(0, 0, true)]
  public void IsNearTail_matches_ps_threshold(int textLength, int charIndexFromBottom, bool expected)
  {
    LogViewerScrollState.IsNearTail(textLength, charIndexFromBottom).Should().Be(expected);
  }
}
