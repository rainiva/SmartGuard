namespace SmartGuard.Configuration;

/// <summary>
/// Writer-confirmed partition of a batch. Lost means unconfirmed, not proof
/// that no bytes reached disk; fallback may replay the original event IDs.
/// </summary>
public readonly record struct LogBatchWriteResult(
  int PrimaryWritten, int FallbackWritten, int Lost, bool PrimaryAttemptFailed)
{
  public int Written => PrimaryWritten + FallbackWritten;
  public bool FullyPersisted => Lost == 0;
  public static LogBatchWriteResult Primary(int count) => new(count, 0, 0, false);
  public static LogBatchWriteResult Failed(int count) => new(0, 0, count, true);
  public bool IsValidFor(int count) => PrimaryWritten >= 0 && FallbackWritten >= 0 && Lost >= 0
    && (long)PrimaryWritten + FallbackWritten + Lost == count;
}
