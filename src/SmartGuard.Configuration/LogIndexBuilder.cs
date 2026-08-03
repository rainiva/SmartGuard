namespace SmartGuard.Configuration;

/// <summary>
/// 日志索引构建器：消费 <see cref="ILogEventSource"/> 构建内存索引。
/// 支持 CancellationToken 取消；解析失败的单行跳过不计入索引。
/// </summary>
public sealed class LogIndexBuilder
{
  public LogIndex Build(ILogEventSource eventSource, string sourcePath, CancellationToken cancellationToken)
  {
    var entries = new List<LogIndexEntry>();

    if (eventSource is not ILogPhysicalLineSource lineSource)
      return new LogIndex(entries, sourcePath);

    try
    {
      foreach (var line in lineSource.EnumeratePhysicalLines(new LogReadScope(Array.Empty<LogSourceIdentity>()), cancellationToken))
      {
        if (cancellationToken.IsCancellationRequested)
          break;

        var entry = FromDecoded(LogEventDecoder.Decode(line), line);
        if (entry is not null)
          entries.Add(entry);
      }
    }
    catch (OperationCanceledException)
    {
      // 取消后返回已构建的部分索引。
    }

    return new LogIndex(entries, sourcePath);
  }

  internal static LogIndexEntry? FromDecoded(LogEvent logEvent, LogPhysicalLine line)
  {
    if (string.IsNullOrWhiteSpace(logEvent.EventId))
      return null;

    return new LogIndexEntry(
      line.Source,
      line.ByteOffset,
      logEvent.EventId,
      logEvent.Timestamp,
      logEvent.Severity.ToString().ToUpperInvariant(),
      logEvent.Component,
      logEvent.Channel,
      logEvent.EventName,
      logEvent.OperationId,
      logEvent.TraceId,
      logEvent.UserActionId,
      logEvent.ProcessId,
      logEvent.ThreadId);
  }
}
