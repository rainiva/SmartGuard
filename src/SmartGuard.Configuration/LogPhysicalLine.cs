namespace SmartGuard.Configuration;

public sealed record LogPhysicalLine(
  LogSourceIdentity Source,
  long ByteOffset,
  string Text,
  bool IsComplete,
  int Ordinal = 0);
