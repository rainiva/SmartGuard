using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class LogViewRichTextRendererTests
{
    [Fact]
    public void SetLines_applies_tag_color_to_info_label()
    {
        RunOnSta(() =>
        {
            var richTextBox = CreateRichTextBox();
            LogViewRichTextRenderer.SetLines(
                richTextBox,
                ["[INFO] 2026-06-21 10:00:00 brightness changed"]);

            var tagRun = FindFirstRun(richTextBox, run => run.Text == "[INFO]");
            tagRun.Should().NotBeNull();
            var expected = LogViewTagPalette.GetTagBrush("INFO");
            tagRun!.Foreground.Should().BeOfType<SolidColorBrush>();
            ((SolidColorBrush)tagRun.Foreground).Color.Should().Be(expected.Color);
        });
    }

    [Fact]
    public void GetPlainText_round_trips_rendered_lines()
    {
        RunOnSta(() =>
        {
            var richTextBox = CreateRichTextBox();
            var lines = new[]
            {
                "[INFO] 2026-06-21 10:00:00 first",
                "[RAW] unstructured line",
            };

            LogViewRichTextRenderer.SetLines(richTextBox, lines);
            LogViewRichTextRenderer.GetPlainText(richTextBox).Should().Be(string.Join(Environment.NewLine, lines));
        });
    }

    private static RichTextBox CreateRichTextBox()
    {
        return new RichTextBox
        {
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
        };
    }

    private static Run? FindFirstRun(RichTextBox richTextBox, Func<Run, bool> predicate)
    {
        foreach (var block in richTextBox.Document.Blocks)
        {
            if (block is not Paragraph paragraph)
                continue;

            foreach (var inline in paragraph.Inlines)
            {
                if (inline is Run run && predicate(run))
                    return run;
            }
        }

        return null;
    }

    private static T RunOnSta<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw error;
        return result;
    }

    private static void RunOnSta(Action action) => RunOnSta(() => { action(); return true; });
}
