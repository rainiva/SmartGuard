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
            paragraph.Inlines.Add(CreateRun($" {body}", LogViewTagPalette.BodyBrush));
            return;
        }

        paragraph.Inlines.Add(CreateRun(line, LogViewTagPalette.BodyBrush));
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
