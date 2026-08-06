namespace SmartGuard.Configuration;

public sealed class LogAggregationService
{
  private int _currentGeneration;

  public int CurrentGeneration => Volatile.Read(ref _currentGeneration);

  public int NotifyGenerationChanged()
  {
    _currentGeneration = unchecked(_currentGeneration + 1);
    return _currentGeneration;
  }

  public async Task<LogAggregationResult> AggregateAsync(
    ILogEventSource eventSource,
    LogAggregationRequest request,
    int generation,
    CancellationToken cancellationToken)
  {
    if (IsSuperseded(generation))
      return LogAggregationResult.Superseded;

    if (cancellationToken.IsCancellationRequested)
      return LogAggregationResult.Cancelled;

    var bucketSize = request.BucketSize <= TimeSpan.Zero
      ? TimeSpan.FromMinutes(10)
      : request.BucketSize;

    var errorBuckets = new Dictionary<DateTimeOffset, int>();
    var componentCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var channelCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var eventNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    DateTimeOffset? minBucket = null;
    DateTimeOffset? maxBucket = null;
    var totalMatched = 0;

    try
    {
      await foreach (var logEvent in eventSource.StreamAsync(
                       new LogReadScope(Array.Empty<LogSourceIdentity>()),
                       cancellationToken))
      {
        if (cancellationToken.IsCancellationRequested)
          return LogAggregationResult.Cancelled;

        if (IsSuperseded(generation))
          return LogAggregationResult.Superseded;

        if (!LogQueryExpressionEvaluator.Matches(logEvent, request.Expression))
          continue;

        totalMatched++;
        IncrementNamedCount(componentCounts, logEvent.Component);
        IncrementNamedCount(channelCounts, logEvent.Channel);
        if (!string.IsNullOrWhiteSpace(logEvent.EventName))
          IncrementNamedCount(eventNameCounts, logEvent.EventName);

        var bucketStart = GetBucketStart(logEvent.Timestamp, bucketSize, request.TimeZone);
        if (minBucket is null || bucketStart < minBucket)
          minBucket = bucketStart;
        if (maxBucket is null || bucketStart > maxBucket)
          maxBucket = bucketStart;

        if (logEvent.Severity is LogSeverity.Error or LogSeverity.Fatal)
          errorBuckets[bucketStart] = errorBuckets.GetValueOrDefault(bucketStart) + 1;
      }
    }
    catch (OperationCanceledException)
    {
      return LogAggregationResult.Cancelled;
    }

    if (IsSuperseded(generation))
      return LogAggregationResult.Superseded;

    if (cancellationToken.IsCancellationRequested)
      return LogAggregationResult.Cancelled;

    var severityBuckets = BuildZeroFilledBuckets(
      errorBuckets,
      minBucket,
      maxBucket,
      bucketSize,
      request.TimeZone);

    return new LogAggregationResult(
      severityBuckets,
      OrderCounts(componentCounts),
      OrderCounts(channelCounts),
      OrderCounts(eventNameCounts),
      totalMatched,
      IsComplete: true,
      IsCancelled: false);
  }

  private bool IsSuperseded(int generation)
  {
    var current = Volatile.Read(ref _currentGeneration);
    return current != 0 && generation != current;
  }

  private static void IncrementNamedCount(Dictionary<string, int> counts, string name)
  {
    counts[name] = counts.GetValueOrDefault(name) + 1;
  }

  private static IReadOnlyList<LogNamedCount> OrderCounts(Dictionary<string, int> counts)
    => counts
      .OrderByDescending(pair => pair.Value)
      .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
      .Select(pair => new LogNamedCount(pair.Key, pair.Value))
      .ToList();

  private static IReadOnlyList<LogSeverityTimeBucket> BuildZeroFilledBuckets(
    Dictionary<DateTimeOffset, int> errorBuckets,
    DateTimeOffset? minBucket,
    DateTimeOffset? maxBucket,
    TimeSpan bucketSize,
    TimeZoneInfo timeZone)
  {
    if (minBucket is null || maxBucket is null)
      return Array.Empty<LogSeverityTimeBucket>();

    var buckets = new List<LogSeverityTimeBucket>();
    for (var cursor = minBucket.Value; cursor <= maxBucket.Value; cursor = AddBucket(cursor, bucketSize, timeZone))
      buckets.Add(new LogSeverityTimeBucket(cursor, errorBuckets.GetValueOrDefault(cursor)));

    return buckets;
  }

  internal static DateTimeOffset GetBucketStart(
    DateTimeOffset timestamp,
    TimeSpan bucketSize,
    TimeZoneInfo timeZone)
  {
    var local = TimeZoneInfo.ConvertTime(timestamp, timeZone);
    var bucketTicks = bucketSize.Ticks;
    var flooredTicks = local.Ticks - local.Ticks % bucketTicks;
    var bucketLocal = new DateTime(flooredTicks, DateTimeKind.Unspecified);
    var offset = timeZone.GetUtcOffset(bucketLocal);
    return new DateTimeOffset(bucketLocal, offset);
  }

  internal static DateTimeOffset AddBucket(
    DateTimeOffset bucketStart,
    TimeSpan bucketSize,
    TimeZoneInfo timeZone)
  {
    var local = TimeZoneInfo.ConvertTime(bucketStart, timeZone);
    var nextLocal = DateTime.SpecifyKind(local.DateTime + bucketSize, DateTimeKind.Unspecified);
    var offset = timeZone.GetUtcOffset(nextLocal);
    return new DateTimeOffset(nextLocal, offset);
  }
}
