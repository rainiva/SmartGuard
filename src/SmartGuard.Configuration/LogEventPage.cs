namespace SmartGuard.Configuration;

public sealed record LogEventPage(
  IReadOnlyList<LogEvent> Events,
  LogReadCursor? OlderCursor,
  LogReadCursor? NewerCursor,
  bool HasOlder,
  bool HasNewer);
