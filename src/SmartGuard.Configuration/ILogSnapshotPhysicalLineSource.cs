namespace SmartGuard.Configuration;

public interface ILogSnapshotPhysicalLineSource : ILogPhysicalLineSource
{
  LogSourceSnapshot CaptureSnapshot();

  IEnumerable<LogPhysicalLine> EnumeratePhysicalLines(
    LogReadScope scope,
    LogSourceSnapshot snapshot,
    LogReadDirection direction,
    LogReadCursor? cursor,
    CancellationToken cancellationToken);
}
