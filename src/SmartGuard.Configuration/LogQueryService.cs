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

  public Task<IReadOnlyList<LogEvent>> ReadPreviewPageAsync(
    ILogEventSource eventSource,
    LogQueryExpression expression,
    LogReadScope scope,
    LogSourceSnapshot snapshot,
    int pageSize,
    bool ignoreCase,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(expression);
    ArgumentNullException.ThrowIfNull(scope);
    ArgumentNullException.ThrowIfNull(snapshot);
    var snapshotSource = RequireSnapshotSource(eventSource);
    cancellationToken.ThrowIfCancellationRequested();

    if (pageSize <= 0)
      return Task.FromResult<IReadOnlyList<LogEvent>>(Array.Empty<LogEvent>());

    return Task.Run(
      () => ReadPreviewPage(
        snapshotSource,
        expression,
        scope,
        snapshot,
        pageSize,
        ignoreCase,
        cancellationToken),
      cancellationToken);
  }

  public Task<LogQueryPageIndex> BuildPageIndexAsync(
    ILogEventSource eventSource,
    LogQueryExpression expression,
    LogReadScope scope,
    LogSourceSnapshot snapshot,
    bool ignoreCase,
    IProgress<LogQueryProgress>? progress,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(expression);
    ArgumentNullException.ThrowIfNull(scope);
    ArgumentNullException.ThrowIfNull(snapshot);
    var snapshotSource = RequireSnapshotSource(eventSource);
    cancellationToken.ThrowIfCancellationRequested();

    return Task.Run(
      () => BuildPageIndex(
        snapshotSource,
        expression,
        scope,
        snapshot,
        ignoreCase,
        progress,
        cancellationToken),
      cancellationToken);
  }

  private static IReadOnlyList<LogEvent> ReadPreviewPage(
    ILogSnapshotPhysicalLineSource snapshotSource,
    LogQueryExpression expression,
    LogReadScope scope,
    LogSourceSnapshot snapshot,
    int pageSize,
    bool ignoreCase,
    CancellationToken cancellationToken)
  {
    var evaluationContext = new LogQueryEvaluationContext(ignoreCase);
    var matches = new List<LogEvent>(pageSize);
    foreach (var line in snapshotSource.EnumeratePhysicalLines(
               scope,
               snapshot,
               LogReadDirection.Latest,
               cursor: null,
               cancellationToken))
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (!line.IsComplete)
        continue;

      var logEvent = LogEventDecoder.Decode(line);
      if (!MatchesSnapshotExpression(logEvent, expression, evaluationContext))
        continue;

      matches.Add(logEvent);
      if (matches.Count >= pageSize)
        break;
    }

    cancellationToken.ThrowIfCancellationRequested();
    return matches;
  }

  private static LogQueryPageIndex BuildPageIndex(
    ILogSnapshotPhysicalLineSource snapshotSource,
    LogQueryExpression expression,
    LogReadScope scope,
    LogSourceSnapshot snapshot,
    bool ignoreCase,
    IProgress<LogQueryProgress>? progress,
    CancellationToken cancellationToken)
  {
    var evaluationContext = new LogQueryEvaluationContext(ignoreCase);
    var anchors = new List<LogQueryAnchor>();
    var scannedCount = 0;
    var matchCount = 0;

    foreach (var line in snapshotSource.EnumeratePhysicalLines(
               scope,
               snapshot,
               LogReadDirection.Newer,
               cursor: null,
               cancellationToken))
    {
      cancellationToken.ThrowIfCancellationRequested();
      scannedCount++;
      if (line.IsComplete)
      {
        var logEvent = LogEventDecoder.Decode(line);
        if (MatchesSnapshotExpression(logEvent, expression, evaluationContext))
        {
          matchCount++;
          if (matchCount % 256 == 0)
          {
            anchors.Add(new LogQueryAnchor(
              matchCount,
              new LogReadCursor(line.Source, line.ByteOffset, logEvent.EventId)));
          }
        }
      }

      ReportProgress(progress, scannedCount, matchCount, isComplete: false, exactTotal: null);
    }

    cancellationToken.ThrowIfCancellationRequested();
    ReportProgress(progress, scannedCount, matchCount, isComplete: true, exactTotal: matchCount);
    return new LogQueryPageIndex(
      snapshot,
      scope,
      expression,
      ignoreCase,
      matchCount,
      scannedCount,
      anchors);
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

  private static ILogSnapshotPhysicalLineSource RequireSnapshotSource(ILogEventSource eventSource)
  {
    ArgumentNullException.ThrowIfNull(eventSource);
    return eventSource as ILogSnapshotPhysicalLineSource
      ?? throw new InvalidOperationException(
        $"Snapshot-backed query operations require {nameof(ILogSnapshotPhysicalLineSource)}.");
  }

  private static bool MatchesSnapshotExpression(
    LogEvent logEvent,
    LogQueryExpression expression,
    LogQueryEvaluationContext evaluationContext)
  {
    evaluationContext.ResetTimeout();
    var matched = LogQueryExpressionEvaluator.Matches(logEvent, expression, evaluationContext);
    if (evaluationContext.RegexTimedOut)
      throw new InvalidOperationException("Regex evaluation timed out after 100ms.");

    return matched;
  }

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
