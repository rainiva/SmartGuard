namespace SmartGuard.Configuration;

public sealed record LogReadRequest(
  LogReadCursor? Cursor,
  LogReadDirection Direction,
  int TargetEventCount,
  int MaxBytes);
