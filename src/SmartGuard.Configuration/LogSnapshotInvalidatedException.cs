namespace SmartGuard.Configuration;

public sealed class LogSnapshotInvalidatedException : IOException
{
  public LogSnapshotInvalidatedException(string message) : base(message)
  {
  }
}
