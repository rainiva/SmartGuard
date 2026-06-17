namespace SmartGuard.LogViewer.Tests;

public class LogViewerTagPaletteTests
{
  [Theory]
  [InlineData("INFO", 0x1A, 0x6F, 0xBF)]
  [InlineData("WARN", 0xC4, 0x7A, 0x00)]
  [InlineData("ERROR", 0xC6, 0x28, 0x28)]
  [InlineData("HEART", 0x5F, 0x6B, 0x7A)]
  public void GetTagColor_maps_known_levels(string tag, byte r, byte g, byte b)
  {
    var color = LogViewerTagPalette.GetTagColor(tag);
    color.R.Should().Be(r);
    color.G.Should().Be(g);
    color.B.Should().Be(b);
  }

  [Fact]
  public void GetTagColor_falls_back_for_unknown_tags()
  {
    LogViewerTagPalette.GetTagColor("DEBUG").Should().Be(LogViewerTagPalette.BodyColor);
  }
}
