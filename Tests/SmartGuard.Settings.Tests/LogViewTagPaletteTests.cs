using System.Windows.Media;
using SmartGuard.LogViewer;

namespace SmartGuard.Settings.Tests;

public class LogViewTagPaletteTests
{
    [Theory]
    [InlineData("INFO", 26, 111, 191)]
    [InlineData("WARN", 196, 122, 0)]
    [InlineData("ERROR", 198, 40, 40)]
    [InlineData("HEART", 95, 107, 122)]
    public void GetTagBrush_matches_log_viewer_palette(string tag, byte r, byte g, byte b)
    {
        var brush = LogViewTagPalette.GetTagBrush(tag);

        brush.Color.R.Should().Be(r);
        brush.Color.G.Should().Be(g);
        brush.Color.B.Should().Be(b);
    }

    [Fact]
    public void GetBodyColor_light_mode_uses_dark_text()
    {
        LogViewTagPalette.ConfigureForDarkMode(false);

        LogViewTagPalette.GetBodyColor().R.Should().BeLessThan(100);
    }

    [Fact]
    public void GetBodyColor_dark_mode_uses_light_gray_text()
    {
        LogViewTagPalette.ConfigureForDarkMode(true);

        LogViewTagPalette.GetBodyColor().Should().Be(Color.FromRgb(0xE8, 0xE8, 0xE8));
    }

    [Fact]
    public void GetTagBrush_RAW_uses_neutral_gray()
    {
        var brush = LogViewTagPalette.GetTagBrush("RAW");
        brush.Color.R.Should().Be(120);
        brush.Color.G.Should().Be(120);
        brush.Color.B.Should().Be(120);
    }
}
