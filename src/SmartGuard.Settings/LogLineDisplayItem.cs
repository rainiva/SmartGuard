using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings;

public sealed class LogLineDisplayItem
{
    private const string NoMatchMessage = "无匹配结果";

    public LogEvent SourceEvent { get; }

    public string EventId => SourceEvent.EventId;

    public string LineText { get; }

    public string? TagLabel { get; }

    public string BodyText { get; }

    public bool IsEmptyState { get; }

    public System.Windows.Media.SolidColorBrush TagBrush { get; }

    public System.Windows.Media.SolidColorBrush BodyBrush { get; }

    private LogLineDisplayItem(
        LogEvent sourceEvent,
        string lineText,
        string? tagLabel,
        string bodyText,
        bool isEmptyState,
        System.Windows.Media.SolidColorBrush tagBrush,
        System.Windows.Media.SolidColorBrush bodyBrush)
    {
        SourceEvent = sourceEvent;
        LineText = lineText;
        TagLabel = tagLabel;
        BodyText = bodyText;
        IsEmptyState = isEmptyState;
        TagBrush = tagBrush;
        BodyBrush = bodyBrush;
    }

    public static LogLineDisplayItem FromEvent(LogEvent logEvent)
    {
        var lineText = LogViewEventFilter.GetDisplayLine(logEvent);
        var tagToken = SeverityToken(logEvent.Severity);
        var bodyText = ExtractBodyText(lineText, tagToken);

        return new LogLineDisplayItem(
            logEvent,
            lineText,
            tagLabel: $"[{tagToken}]",
            bodyText: bodyText,
            isEmptyState: false,
            LogViewTagPalette.GetTagBrush(tagToken),
            LogViewTagPalette.GetBodyBrush());
    }

    public static LogLineDisplayItem Parse(string line)
    {
        if (line == NoMatchMessage)
        {
            return EmptyStateItem(line);
        }

        if (LogLineTagParser.TryParse(line, out var tag, out var body))
        {
            return FromParsedLine(line, tag, body);
        }

        return FromParsedLine(line, tag: null, body: line);
    }

    private static LogLineDisplayItem EmptyStateItem(string line)
    {
        var emptyEvent = CreateCompatibilityEvent("empty-state", line, LogSeverity.Info);
        return new LogLineDisplayItem(
            emptyEvent,
            line,
            tagLabel: null,
            bodyText: line,
            isEmptyState: true,
            LogViewTagPalette.GetTagBrush("RAW"),
            LogViewTagPalette.GetTagBrush("RAW"));
    }

    private static LogLineDisplayItem FromParsedLine(string line, string? tag, string body)
    {
        var severity = ParseSeverity(tag);
        var compatibilityEvent = CreateCompatibilityEvent(
            LogEventId.ComputeLegacyEventId(
                LogSourceIdentity.FromContentPrefix(DateTimeOffset.UnixEpoch, "display-line"),
                0,
                line,
                0),
            body,
            severity);

        var tagToken = tag ?? SeverityToken(severity);
        return new LogLineDisplayItem(
            compatibilityEvent,
            line,
            tagLabel: tag is null ? null : $"[{tag}]",
            bodyText: tag is null ? body : $" {body}",
            isEmptyState: false,
            LogViewTagPalette.GetTagBrush(tag ?? "RAW"),
            tag is null
                ? LogViewTagPalette.GetBodyBrush()
                : LogViewTagPalette.GetBodyBrush());
    }

    private static LogEvent CreateCompatibilityEvent(string eventId, string message, LogSeverity severity)
        => new(
            LogEvent.CurrentSchemaVersion,
            eventId,
            DateTimeOffset.UtcNow,
            severity,
            LogComponentRegistry.Legacy,
            LogChannelRegistry.Compatibility,
            EventName: null,
            message,
            Exception: null,
            OperationId: null,
            TraceId: null,
            SpanId: null,
            UserActionId: null,
            ProcessId: null,
            ThreadId: null,
            LogEvent.EmptyProperties);

    private static LogSeverity ParseSeverity(string? tag)
        => tag?.ToUpperInvariant() switch
        {
            "TRACE" => LogSeverity.Trace,
            "DEBUG" => LogSeverity.Debug,
            "WARN" or "WARNING" => LogSeverity.Warn,
            "ERROR" => LogSeverity.Error,
            "FATAL" or "CRITICAL" => LogSeverity.Fatal,
            "HEART" => LogSeverity.Info,
            _ => LogSeverity.Info,
        };

    private static string SeverityToken(LogSeverity severity)
        => severity switch
        {
            LogSeverity.Trace => "TRACE",
            LogSeverity.Debug => "DEBUG",
            LogSeverity.Info => "INFO",
            LogSeverity.Warn => "WARN",
            LogSeverity.Error => "ERROR",
            LogSeverity.Fatal => "FATAL",
            _ => "INFO",
        };

    private static string ExtractBodyText(string lineText, string tagToken)
    {
        if (LogLineTagParser.TryParse(lineText, out _, out var body))
            return $" {body}";

        return lineText.StartsWith('[')
            ? lineText[(tagToken.Length + 2)..].TrimStart()
            : lineText;
    }
}
