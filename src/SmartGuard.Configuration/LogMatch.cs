namespace SmartGuard.Configuration;

public sealed record LogMatch(
  int Ordinal,
  string EventId,
  LogSourceIdentity Source,
  long ByteOffset);
