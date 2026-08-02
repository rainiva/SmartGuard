namespace SmartGuard.Configuration;

public sealed record LogExceptionInfo(
  string Type,
  string Message,
  string? StackTrace,
  LogExceptionInfo? Inner);
