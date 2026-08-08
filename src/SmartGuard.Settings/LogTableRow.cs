using System.Globalization;
using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings;

/// <summary>
/// Pure text projection for one event in the paged log table.
/// </summary>
public sealed class LogTableRow
{
  public const string UnknownStatusText = "—";

  private LogTableRow(
    LogEvent sourceEvent,
    string levelText,
    string timeText,
    string sourceText,
    string eventText,
    string statusText,
    string exportText)
  {
    SourceEvent = sourceEvent;
    LevelText = levelText;
    TimeText = timeText;
    SourceText = sourceText;
    EventText = eventText;
    StatusText = statusText;
    ExportText = exportText;
  }

  public LogEvent SourceEvent { get; }

  public string EventId => SourceEvent.EventId;

  public string LevelText { get; }

  public string TimeText { get; }

  public string SourceText { get; }

  public string EventText { get; }

  public string StatusText { get; }

  public string ExportText { get; }

  public bool IsExceptionRoot => LogExceptionGroupProjector.IsExceptionRoot(SourceEvent);

  public string? ExceptionGroupRootEventId => IsExceptionRoot ? SourceEvent.EventId : null;

  public LogOperationCorrelationKey? CorrelationKey =>
    LogOperationGraph.ResolveCorrelationKey(SourceEvent);

  public bool HasCorrelationKey => CorrelationKey is not null;

  public static LogTableRow FromEvent(LogEvent sourceEvent)
  {
    ArgumentNullException.ThrowIfNull(sourceEvent);

    return new LogTableRow(
      sourceEvent,
      LevelTextFor(sourceEvent.Severity),
      sourceEvent.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
      sourceEvent.Component,
      BuildEventText(sourceEvent),
      ResolveStatus(sourceEvent),
      BuildExportText(sourceEvent));
  }

  private static string BuildEventText(LogEvent sourceEvent)
  {
    if (string.IsNullOrWhiteSpace(sourceEvent.EventName))
      return sourceEvent.Message;
    if (string.IsNullOrWhiteSpace(sourceEvent.Message))
      return sourceEvent.EventName!;

    return $"{sourceEvent.EventName}：{sourceEvent.Message}";
  }

  private static string ResolveStatus(LogEvent sourceEvent)
  {
    // Task 0 scanned 12 real events and found no confirmed status key/value.
    // Do not infer a status from severity or message until a real key is registered.
    _ = sourceEvent;
    return UnknownStatusText;
  }

  private static string BuildExportText(LogEvent sourceEvent)
    => sourceEvent.Exception is null
      ? LogViewEventFilter.GetDisplayLine(sourceEvent)
      : string.Join(Environment.NewLine, sourceEvent.Exception.FormatExpandedLines());

  private static string LevelTextFor(LogSeverity severity)
    => severity switch
    {
      LogSeverity.Trace => "跟踪",
      LogSeverity.Debug => "调试",
      LogSeverity.Info => "信息",
      LogSeverity.Warn => "警告",
      LogSeverity.Error => "错误",
      LogSeverity.Fatal => "致命",
      _ => "信息",
    };
}
