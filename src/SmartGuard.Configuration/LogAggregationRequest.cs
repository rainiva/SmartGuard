namespace SmartGuard.Configuration;

public sealed record LogAggregationRequest(
  LogQueryExpression Expression,
  TimeSpan BucketSize,
  TimeZoneInfo TimeZone);
