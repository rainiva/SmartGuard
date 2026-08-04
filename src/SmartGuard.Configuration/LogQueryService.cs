using System.Runtime.CompilerServices;

namespace SmartGuard.Configuration;

/// <summary>
/// 日志查询服务：编译并执行 <see cref="LogQueryExpression"/>，支持内存索引与流式历史扫描。
/// </summary>
public sealed class LogQueryService
{
  public LogQueryResult Query(LogIndex index, LogQueryRequest request, CancellationToken cancellationToken)
  {
    var expression = request.ToExpression();
    var filtered = index.Entries.Where(entry => LogQueryExpressionEvaluator.Matches(entry, expression));

    var materialized = filtered.ToList();

    if (cancellationToken.IsCancellationRequested)
      return new LogQueryResult(Array.Empty<LogIndexEntry>(), 0, request.Page, request.PageSize, IsScanComplete: false);

    var totalCount = materialized.Count;
    var pageSize = Math.Max(1, request.PageSize);
    var page = Math.Max(1, request.Page);
    var skip = (page - 1) * pageSize;
    var pageItems = skip < totalCount
      ? materialized.Skip(skip).Take(pageSize).ToList()
      : new List<LogIndexEntry>();

    return new LogQueryResult(pageItems, totalCount, page, pageSize, totalCount, totalCount, true);
  }

  public Task<LogQueryResult> QueryAsync(
    ILogEventSource eventSource,
    LogQueryExpression expression,
    LogQueryCursor? cursor,
    int pageSize,
    IProgress<LogQueryProgress>? progress,
    CancellationToken cancellationToken)
    => QueryAsync(eventSource, expression, cursor, pageSize, progress, cancellationToken, ignoreCase: true);

  public async Task<LogQueryResult> QueryAsync(
    ILogEventSource eventSource,
    LogQueryExpression expression,
    LogQueryCursor? cursor,
    int pageSize,
    IProgress<LogQueryProgress>? progress,
    CancellationToken cancellationToken,
    bool ignoreCase,
    string? originalExpressionText = null)
  {
    pageSize = Math.Max(1, pageSize);
    if (originalExpressionText is not null)
    {
      var validation = LogQueryExpressionValidator.Validate(expression, originalExpressionText);
      if (!validation.IsValid)
      {
        return new LogQueryResult(
          Array.Empty<LogIndexEntry>(),
          0,
          1,
          pageSize,
          ErrorMessage: validation.ErrorMessage);
      }
    }

    var evaluationContext = new LogQueryEvaluationContext(ignoreCase);
    var matches = new List<LogIndexEntry>();
    var orderedMatches = new List<LogMatch>();
    var scannedCount = 0;
    var matchCount = 0;
    var skipUntilCursor = cursor is not null;
    LogQueryCursor? nextCursor = null;

    bool EventMatches(LogEvent logEvent)
    {
      evaluationContext.ResetTimeout();
      var matched = LogQueryExpressionEvaluator.Matches(logEvent, expression, evaluationContext);
      if (evaluationContext.RegexTimedOut)
        return false;
      return matched;
    }

    if (eventSource is ILogPhysicalLineSource lineSource)
    {
      foreach (var line in lineSource.EnumeratePhysicalLines(new LogReadScope(Array.Empty<LogSourceIdentity>()), cancellationToken))
      {
        if (cancellationToken.IsCancellationRequested)
          return CreatePartialResult(matches, matchCount, pageSize, false, nextCursor, orderedMatches, scannedCount);

        scannedCount++;
        var logEvent = LogEventDecoder.Decode(line);
        if (skipUntilCursor)
        {
          if (IsCursorMatch(cursor!, line, logEvent.EventId))
            skipUntilCursor = false;
          else
            continue;
        }

        if (!EventMatches(logEvent))
        {
          if (evaluationContext.RegexTimedOut)
          {
            return CreateTimeoutResult(
              matches,
              matchCount,
              pageSize,
              nextCursor,
              orderedMatches,
              scannedCount);
          }

          ReportProgress(progress, scannedCount, matchCount, false, null);
          continue;
        }

        matchCount++;
        orderedMatches.Add(new LogMatch(matchCount, logEvent.EventId, line.Source, line.ByteOffset));
        var entry = LogIndexBuilder.FromDecoded(logEvent, line);
        if (entry is not null && matches.Count < pageSize)
          matches.Add(entry);

        nextCursor = new LogQueryCursor(line.Source, line.ByteOffset, logEvent.EventId, matchCount);
        ReportProgress(progress, scannedCount, matchCount, false, null);
      }
    }
    else
    {
      try
      {
        await foreach (var logEvent in eventSource.StreamAsync(new LogReadScope(Array.Empty<LogSourceIdentity>()), cancellationToken))
        {
          if (cancellationToken.IsCancellationRequested)
            return CreatePartialResult(matches, matchCount, pageSize, false, nextCursor, orderedMatches, scannedCount);

          scannedCount++;
          if (skipUntilCursor)
          {
            if (string.Equals(cursor!.EventId, logEvent.EventId, StringComparison.Ordinal))
              skipUntilCursor = false;
            else
              continue;
          }

          if (!EventMatches(logEvent))
          {
            if (evaluationContext.RegexTimedOut)
            {
              return CreateTimeoutResult(
                matches,
                matchCount,
                pageSize,
                nextCursor,
                orderedMatches,
                scannedCount);
            }

            ReportProgress(progress, scannedCount, matchCount, false, null);
            continue;
          }

          matchCount++;
          var entry = CreateSparseEntry(logEvent);
          orderedMatches.Add(new LogMatch(matchCount, logEvent.EventId, entry.Source, entry.ByteOffset));
          if (matches.Count < pageSize)
            matches.Add(entry);

          nextCursor = new LogQueryCursor(entry.Source, entry.ByteOffset, logEvent.EventId, matchCount);
          ReportProgress(progress, scannedCount, matchCount, false, null);
        }
      }
      catch (OperationCanceledException)
      {
        return CreatePartialResult(matches, matchCount, pageSize, false, nextCursor, orderedMatches, scannedCount);
      }
    }

    ReportProgress(progress, scannedCount, matchCount, true, matchCount);
    return new LogQueryResult(
      matches,
      matchCount,
      1,
      pageSize,
      matchCount,
      matchCount,
      true,
      nextCursor,
      orderedMatches,
      ScannedCount: scannedCount);
  }

  private static LogQueryResult CreatePartialResult(
    IReadOnlyList<LogIndexEntry> matches,
    int matchCount,
    int pageSize,
    bool isComplete,
    LogQueryCursor? nextCursor,
    IReadOnlyList<LogMatch> orderedMatches,
    int scannedCount)
    => new(
      matches,
      matchCount,
      1,
      pageSize,
      matchCount,
      null,
      isComplete,
      nextCursor,
      orderedMatches,
      ScannedCount: scannedCount);

  private static LogQueryResult CreateTimeoutResult(
    IReadOnlyList<LogIndexEntry> matches,
    int matchCount,
    int pageSize,
    LogQueryCursor? nextCursor,
    IReadOnlyList<LogMatch> orderedMatches,
    int scannedCount)
    => new(
      matches,
      matchCount,
      1,
      pageSize,
      matchCount,
      null,
      false,
      nextCursor,
      orderedMatches,
      ErrorMessage: "Regex evaluation timed out after 100ms.",
      AbortedDueToRegexTimeout: true,
      ScannedCount: scannedCount);

  private static void ReportProgress(
    IProgress<LogQueryProgress>? progress,
    int scannedCount,
    int matchCount,
    bool isComplete,
    int? exactTotal)
  {
    progress?.Report(new LogQueryProgress(scannedCount, matchCount, isComplete, exactTotal));
  }

  private static bool IsCursorMatch(LogQueryCursor cursor, LogPhysicalLine line, string eventId)
    => cursor.Source.Equals(line.Source)
       && cursor.ByteOffset == line.ByteOffset
       && string.Equals(cursor.EventId, eventId, StringComparison.Ordinal);

  internal static LogIndexEntry CreateSparseEntry(LogEvent logEvent)
  {
    var source = LogSourceIdentity.FromContentPrefix(logEvent.Timestamp, logEvent.EventId);
    return new LogIndexEntry(
      source,
      0,
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

  public async Task<IReadOnlyList<LogEvent>> QueryMatchingEventsAsync(
    ILogEventSource eventSource,
    LogQueryExpression expression,
    CancellationToken cancellationToken)
  {
    var matches = new List<LogEvent>();

    await foreach (var logEvent in eventSource.StreamAsync(
                     new LogReadScope(Array.Empty<LogSourceIdentity>()),
                     cancellationToken))
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (LogQueryExpressionEvaluator.Matches(logEvent, expression))
        matches.Add(logEvent);
    }

    return LogOperationProjector.OrderCorrelatedEvents(matches);
  }

  public async Task<IReadOnlyList<LogSessionMetadata>> ProjectSessionsAsync(
    ILogEventSource eventSource,
    CancellationToken cancellationToken)
  {
    var builder = LogSessionProjector.CreateProjectionBuilder();
    await foreach (var logEvent in eventSource.StreamAsync(
                     new LogReadScope(Array.Empty<LogSourceIdentity>()),
                     cancellationToken))
    {
      cancellationToken.ThrowIfCancellationRequested();
      builder.Add(logEvent);
    }

    return builder.Build();
  }
}
