namespace SmartGuard.Configuration;

public sealed record LogSeverityTimeBucket(
  DateTimeOffset BucketStart,
  int ErrorCount);

public sealed record LogNamedCount(
  string Name,
  int Count);

public sealed record LogAggregationResult(
  IReadOnlyList<LogSeverityTimeBucket> SeverityBuckets,
  IReadOnlyList<LogNamedCount> ComponentCounts,
  IReadOnlyList<LogNamedCount> ChannelCounts,
  IReadOnlyList<LogNamedCount> EventNameCounts,
  int TotalMatchedEvents,
  bool IsComplete,
  bool IsCancelled,
  bool IsSuperseded = false)
{
  public static LogAggregationResult Cancelled { get; } = new(
    Array.Empty<LogSeverityTimeBucket>(),
    Array.Empty<LogNamedCount>(),
    Array.Empty<LogNamedCount>(),
    Array.Empty<LogNamedCount>(),
    TotalMatchedEvents: 0,
    IsComplete: false,
    IsCancelled: true);

  public static LogAggregationResult Superseded { get; } = new(
    Array.Empty<LogSeverityTimeBucket>(),
    Array.Empty<LogNamedCount>(),
    Array.Empty<LogNamedCount>(),
    Array.Empty<LogNamedCount>(),
    TotalMatchedEvents: 0,
    IsComplete: false,
    IsCancelled: false,
    IsSuperseded: true);
}
