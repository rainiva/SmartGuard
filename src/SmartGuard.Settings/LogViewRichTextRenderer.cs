using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SmartGuard.LogViewer;

namespace SmartGuard.Settings;

public static class LogViewRichTextRenderer
{
    private const double NoWrapPageWidth = 100_000;
    private const string NoMatchMessage = "无匹配结果";
    private const string NoLevelFilterMessage = "请至少选择一种日志级别";

    public static void SetLines(RichTextBox richTextBox, IReadOnlyList<string> lines)
    {
        EnsureDocument(richTextBox);
        richTextBox.Document!.Blocks.Clear();

        foreach (var line in lines)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            AppendLine(paragraph, line);
            richTextBox.Document.Blocks.Add(paragraph);
        }
    }

    public static string GetPlainText(RichTextBox richTextBox)
    {
        if (richTextBox.Document is null)
            return string.Empty;

        var text = new TextRange(richTextBox.Document.ContentStart, richTextBox.Document.ContentEnd).Text;
        return text.TrimEnd('\r', '\n');
    }

    public static void SynchronizeViewport(RichTextBox richTextBox, double viewportWidth)
    {
        EnsureDocument(richTextBox);

        if (viewportWidth <= 0 || double.IsNaN(viewportWidth))
            return;

        var contentWidth = MeasureContentWidth(richTextBox);
        richTextBox.Width = Math.Max(viewportWidth, contentWidth);
        richTextBox.MinWidth = viewportWidth;
        richTextBox.InvalidateMeasure();
        richTextBox.InvalidateArrange();
        richTextBox.UpdateLayout();
    }

    public static double MeasureContentWidth(RichTextBox richTextBox)
    {
        EnsureDocument(richTextBox);

        double maxWidth = 0;
        foreach (Block block in richTextBox.Document!.Blocks)
        {
            if (block is not Paragraph paragraph)
                continue;

            var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.TrimEnd('\r', '\n');
            maxWidth = Math.Max(maxWidth, MeasureTextWidth(richTextBox, text));
        }

        var padding = richTextBox.Padding;
        return maxWidth + padding.Left + padding.Right;
    }

    private static double MeasureTextWidth(RichTextBox richTextBox, string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var typeface = new Typeface(
            richTextBox.FontFamily,
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);

        var pixelsPerDip = 1.0;
        if (richTextBox.IsLoaded)
            pixelsPerDip = VisualTreeHelper.GetDpi(richTextBox).PixelsPerDip;

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            richTextBox.FontSize,
            Brushes.Black,
            pixelsPerDip);

        return formatted.WidthIncludingTrailingWhitespace;
    }

    private static void EnsureDocument(RichTextBox richTextBox)
    {
        richTextBox.Document ??= new FlowDocument();
        richTextBox.Document.PageWidth = NoWrapPageWidth;
        richTextBox.Document.PagePadding = new Thickness(0);
    }

    private static void AppendLine(Paragraph paragraph, string line)
    {
        if (IsEmptyStateMessage(line))
        {
            paragraph.Inlines.Add(new Run(line)
            {
                Foreground = LogViewTagPalette.GetTagBrush("RAW"),
                FontStyle = FontStyles.Italic,
            });
            return;
        }

        if (LogLineTagParser.TryParse(line, out var tag, out var body))
        {
            paragraph.Inlines.Add(CreateRun($"[{tag}]", LogViewTagPalette.GetTagBrush(tag)));
            paragraph.Inlines.Add(CreateRun($" {body}", LogViewTagPalette.GetBodyBrush()));
            return;
        }

        paragraph.Inlines.Add(CreateRun(line, LogViewTagPalette.GetBodyBrush()));
    }

    private static Run CreateRun(string text, SolidColorBrush foreground)
    {
        return new Run(text) { Foreground = foreground };
    }

    private static bool IsEmptyStateMessage(string line)
    {
        return line is NoMatchMessage or NoLevelFilterMessage;
    }
}
