using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings;

public sealed class LogLineDisplayItem
{
    private const string NoMatchMessage = "无匹配结果";

    public LogEvent SourceEvent { get; }

    public string EventId => SourceEvent.EventId;

    public string LineText { get; }

    public string ExportLineText { get; }

    public string? TagLabel { get; }

    public string BodyText { get; }

    public bool IsEmptyState { get; }

    public bool IsExceptionRoot { get; }

    public bool IsExceptionContinuation { get; }

    public bool IsExceptionFolded { get; }

    public string? ExceptionGroupRootEventId { get; }

    public bool HasCorrelationKey => LogOperationGraph.ResolveCorrelationKey(SourceEvent) is not null;

    public System.Windows.Media.SolidColorBrush TagBrush { get; }

    public System.Windows.Media.SolidColorBrush BodyBrush { get; }

    private LogLineDisplayItem(
        LogEvent sourceEvent,
        string lineText,
        string exportLineText,
        string? tagLabel,
        string bodyText,
        bool isEmptyState,
        bool isExceptionRoot,
        bool isExceptionContinuation,
        bool isExceptionFolded,
        string? exceptionGroupRootEventId,
        System.Windows.Media.SolidColorBrush tagBrush,
        System.Windows.Media.SolidColorBrush bodyBrush)
    {
        SourceEvent = sourceEvent;
        LineText = lineText;
        ExportLineText = exportLineText;
        TagLabel = tagLabel;
        BodyText = bodyText;
        IsEmptyState = isEmptyState;
        IsExceptionRoot = isExceptionRoot;
        IsExceptionContinuation = isExceptionContinuation;
        IsExceptionFolded = isExceptionFolded;
        ExceptionGroupRootEventId = exceptionGroupRootEventId;
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
            lineText,
            tagLabel: $"[{tagToken}]",
            bodyText: bodyText,
            isEmptyState: false,
            isExceptionRoot: false,
            isExceptionContinuation: false,
            isExceptionFolded: false,
            exceptionGroupRootEventId: null,
            LogViewTagPalette.GetTagBrush(tagToken),
            LogViewTagPalette.GetBodyBrush());
    }

    public static LogLineDisplayItem FromExceptionGroup(
        LogExceptionGroup group,
        bool expanded,
        bool includeContinuations)
    {
        var root = group.RootEvent;
        var lineText = LogViewEventFilter.GetDisplayLine(root);
        var tagToken = SeverityToken(root.Severity);
        var exportLineText = BuildExportText(group, includeContinuations: true);
        var bodyText = expanded
            ? ExtractBodyText(lineText, tagToken)
            : BuildCollapsedBodyText(group);

        return new LogLineDisplayItem(
            root,
            lineText,
            exportLineText,
            tagLabel: $"[{tagToken}]",
            bodyText: bodyText,
            isEmptyState: false,
            isExceptionRoot: true,
            isExceptionContinuation: false,
            isExceptionFolded: !expanded,
            exceptionGroupRootEventId: root.EventId,
            LogViewTagPalette.GetTagBrush(tagToken),
            LogViewTagPalette.GetBodyBrush());
    }

    public static LogLineDisplayItem FromExceptionContinuation(
        LogExceptionGroup group,
        LogEvent continuationEvent)
    {
        var lineText = LogViewEventFilter.GetDisplayLine(continuationEvent);
        var tagToken = SeverityToken(continuationEvent.Severity);

        return new LogLineDisplayItem(
            continuationEvent,
            lineText,
            lineText,
            tagLabel: null,
            bodyText: $"   {continuationEvent.Message.TrimStart()}",
            isEmptyState: false,
            isExceptionRoot: false,
            isExceptionContinuation: true,
            isExceptionFolded: false,
            exceptionGroupRootEventId: group.RootEvent.EventId,
            LogViewTagPalette.GetBodyBrush(),
            LogViewTagPalette.GetBodyBrush());
    }

    public static LogLineDisplayItem FromStructuredExceptionLine(LogExceptionGroup group, string line)
    {
        var syntheticEvent = CreateCompatibilityEvent(
            $"{group.RootEvent.EventId}:stack:{line.GetHashCode()}",
            line,
            group.RootEvent.Severity);

        return new LogLineDisplayItem(
            syntheticEvent,
            line,
            line,
            tagLabel: null,
            bodyText: $"   {line}",
            isEmptyState: false,
            isExceptionRoot: false,
            isExceptionContinuation: true,
            isExceptionFolded: false,
            exceptionGroupRootEventId: group.RootEvent.EventId,
            LogViewTagPalette.GetBodyBrush(),
            LogViewTagPalette.GetBodyBrush());
    }

    public static LogLineDisplayItem Parse(string line)
    {
        if (line == NoMatchMessage)
            return EmptyStateItem(line);

        if (LogLineTagParser.TryParse(line, out var tag, out var body))
            return FromParsedLine(line, tag, body);

        return FromParsedLine(line, tag: null, body: line);
    }

    private static LogLineDisplayItem EmptyStateItem(string line)
    {
        var emptyEvent = CreateCompatibilityEvent("empty-state", line, LogSeverity.Info);
        return new LogLineDisplayItem(
            emptyEvent,
            line,
            line,
            tagLabel: null,
            bodyText: line,
            isEmptyState: true,
            isExceptionRoot: false,
            isExceptionContinuation: false,
            isExceptionFolded: false,
            exceptionGroupRootEventId: null,
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
            line,
            tagLabel: tag is null ? null : $"[{tag}]",
            bodyText: tag is null ? body : $" {body}",
            isEmptyState: false,
            isExceptionRoot: false,
            isExceptionContinuation: false,
            isExceptionFolded: false,
            exceptionGroupRootEventId: null,
            LogViewTagPalette.GetTagBrush(tag ?? "RAW"),
            tag is null
                ? LogViewTagPalette.GetBodyBrush()
                : LogViewTagPalette.GetBodyBrush());
    }

    private static string BuildCollapsedBodyText(LogExceptionGroup group)
    {
        if (group.IsStructured && group.RootEvent.Exception is not null)
        {
            var exception = group.RootEvent.Exception;
            var innerSuffix = group.InnerCount > 0 ? $" (+{group.InnerCount} inner)" : string.Empty;
            return $" {exception.Type}: {exception.Message}{innerSuffix} ▸";
        }

        var hiddenCount = group.ContinuationEvents.Count;
        var suffix = hiddenCount > 0 ? $" (+{hiddenCount} stack lines) ▸" : " ▸";
        return $" {group.RootEvent.Message.TrimStart()}{suffix}";
    }

    internal static string BuildExportText(LogExceptionGroup group, bool includeContinuations)
    {
        var lines = new List<string>();
        if (group.IsStructured && group.RootEvent.Exception is not null)
        {
            lines.AddRange(group.RootEvent.Exception.FormatExpandedLines());
        }
        else
        {
            lines.Add(LogViewEventFilter.GetDisplayLine(group.RootEvent));
            if (includeContinuations)
            {
                foreach (var continuation in group.ContinuationEvents)
                    lines.Add(LogViewEventFilter.GetDisplayLine(continuation));
            }
        }

        return string.Join(Environment.NewLine, lines);
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
