using SmartGuard.LogViewer;

namespace SmartGuard.Settings;

public sealed class LogLineDisplayItem
{
    private const string NoMatchMessage = "无匹配结果";

    public string LineText { get; }
    public string? TagLabel { get; }
    public string BodyText { get; }
    public bool IsEmptyState { get; }
    public System.Windows.Media.SolidColorBrush TagBrush { get; }
    public System.Windows.Media.SolidColorBrush BodyBrush { get; }

    private LogLineDisplayItem(
        string lineText,
        string? tagLabel,
        string bodyText,
        bool isEmptyState,
        System.Windows.Media.SolidColorBrush tagBrush,
        System.Windows.Media.SolidColorBrush bodyBrush)
    {
        LineText = lineText;
        TagLabel = tagLabel;
        BodyText = bodyText;
        IsEmptyState = isEmptyState;
        TagBrush = tagBrush;
        BodyBrush = bodyBrush;
    }

    public static LogLineDisplayItem Parse(string line)
    {
        if (line == NoMatchMessage)
        {
            return new LogLineDisplayItem(
                line,
                tagLabel: null,
                bodyText: line,
                isEmptyState: true,
                LogViewTagPalette.GetTagBrush("RAW"),
                LogViewTagPalette.GetTagBrush("RAW"));
        }

        if (LogLineTagParser.TryParse(line, out var tag, out var body))
        {
            return new LogLineDisplayItem(
                line,
                tagLabel: $"[{tag}]",
                bodyText: $" {body}",
                isEmptyState: false,
                LogViewTagPalette.GetTagBrush(tag),
                LogViewTagPalette.GetBodyBrush());
        }

        return new LogLineDisplayItem(
            line,
            tagLabel: null,
            bodyText: line,
            isEmptyState: false,
            LogViewTagPalette.GetBodyBrush(),
            LogViewTagPalette.GetBodyBrush());
    }
}
