using System.Windows.Media;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class LogLineDisplayItemTests
{
    [Fact]
    public void Parse_applies_tag_color_to_info_label()
    {
        var item = LogLineDisplayItem.Parse("[INFO] 2026-06-21 10:00:00 brightness changed");

        item.TagLabel.Should().Be("[INFO]");
        item.TagBrush.Color.Should().Be(LogViewTagPalette.GetTagBrush("INFO").Color);
        item.BodyText.Should().Contain("brightness changed");
    }

    [Fact]
    public void Parse_preserves_full_line_text_for_export()
    {
        var lines = new[]
        {
            "[INFO] 2026-06-21 10:00:00 first",
            "[RAW] unstructured line",
        };

        string.Join(Environment.NewLine, lines.Select(LogLineDisplayItem.Parse).Select(item => item.LineText))
            .Should().Be(string.Join(Environment.NewLine, lines));
    }
}
