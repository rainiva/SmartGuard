namespace SmartGuard.Configuration;

public sealed record LogQueryProgress(
  int ScannedCount,
  int MatchCount,
  bool IsComplete,
  int? ExactTotal);
