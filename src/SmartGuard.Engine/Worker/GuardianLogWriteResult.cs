namespace SmartGuard.Engine.Worker;

internal enum GuardianLogWriteResult
{
  SuppressedByLevel,
  AcceptedByPipeline,
  AcceptedByFallback,
  DroppedByBackpressure,
  Rejected,
}

internal static class GuardianLogWriteResultExtensions
{
  public static bool IsAccepted(this GuardianLogWriteResult result)
    => result is GuardianLogWriteResult.AcceptedByPipeline
      or GuardianLogWriteResult.AcceptedByFallback;
}
