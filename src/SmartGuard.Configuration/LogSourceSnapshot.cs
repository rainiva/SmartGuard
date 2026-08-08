namespace SmartGuard.Configuration;

public sealed record LogSourceSnapshotEntry(
  LogSourceIdentity Source,
  LogSourceKind Kind,
  long CapturedLength,
  string BoundaryFingerprint);

public sealed record LogSourceSnapshot(
  IReadOnlyList<LogSourceSnapshotEntry> Sources,
  string Fingerprint);
