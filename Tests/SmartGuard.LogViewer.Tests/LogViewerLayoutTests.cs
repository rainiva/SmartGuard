namespace SmartGuard.LogViewer.Tests;

public class LogViewerLayoutTests
{
  [Fact]
  public void ContentPadding_provides_readable_margin_around_log_text()
  {
    LogViewerLayout.TextLeftMargin.Should().BeGreaterThanOrEqualTo(12);
    LogViewerLayout.TextRightMargin.Should().BeGreaterThanOrEqualTo(12);
    LogViewerLayout.TextTopMargin.Should().BeGreaterThanOrEqualTo(8);
    LogViewerLayout.TextBottomMargin.Should().BeGreaterThanOrEqualTo(8);
  }
}
