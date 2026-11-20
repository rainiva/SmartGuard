namespace SmartGuard.Configuration;

public sealed record LogQueryCursor(
  LogSourceIdentity Source,
  long ByteOffset,
  string EventId,
  int MatchOrdinal);
