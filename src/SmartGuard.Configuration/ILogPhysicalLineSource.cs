namespace SmartGuard.Configuration;

public interface ILogPhysicalLineSource
{
  IEnumerable<LogPhysicalLine> EnumeratePhysicalLines(
    LogReadScope scope,
    CancellationToken cancellationToken);

  IAsyncEnumerable<LogPhysicalLine> StreamPhysicalLinesAsync(
    LogReadScope scope,
    CancellationToken cancellationToken);
}
