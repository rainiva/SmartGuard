namespace SmartGuard.Configuration;

public enum LogSourceKind
{
  Current,
  Archive,
  StartupFallback,
}

public sealed record LogSourceDescriptor(
  string FilePath,
  LogSourceIdentity Identity,
  LogSourceKind Kind);
