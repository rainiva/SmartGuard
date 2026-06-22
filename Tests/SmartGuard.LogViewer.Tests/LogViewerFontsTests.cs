namespace SmartGuard.LogViewer.Tests;

[Collection("LogViewerWinForms")]
public class LogViewerFontsTests
{
  [Fact]
  public void Body_uses_unified_cjk_friendly_font_instead_of_consolas()
  {
    var font = LogViewerFonts.Body;
    font.FontFamily.Name.Should().Be("Microsoft YaHei UI");
    font.Size.Should().BeGreaterThan(9f);
  }
}
