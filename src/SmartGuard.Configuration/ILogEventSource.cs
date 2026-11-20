namespace SmartGuard.Configuration;

public interface ILogEventSource
{
  Task<LogEventPage> ReadLatestAsync(LogReadRequest request, CancellationToken cancellationToken);

  Task<LogEventPage> ReadOlderAsync(LogReadRequest request, CancellationToken cancellationToken);

  Task<LogEventPage> ReadNewerAsync(LogReadRequest request, CancellationToken cancellationToken);

  IAsyncEnumerable<LogEvent> StreamAsync(LogReadScope scope, CancellationToken cancellationToken);
}

public sealed record LogReadScope(IReadOnlyList<LogSourceIdentity> Sources);
