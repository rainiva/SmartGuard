namespace SmartGuard.Engine.Infrastructure;

internal enum PowerPlanFailureKind
{
  None,
  ProcessStartFailed,
  TimedOut,
  CommandFailed,
  ParseFailed,
  VerificationMismatch,
}

internal readonly record struct PowerPlanProbeResult(
  bool Succeeded,
  PowerSchemeInfo? Plan,
  PowerPlanFailureKind FailureKind,
  string Diagnostic)
{
  public Guid? ObservedGuid => Plan?.Guid;
}

internal readonly record struct PowerPlanApplyResult(
  bool Succeeded,
  Guid TargetGuid,
  Guid? ObservedGuid,
  PowerPlanFailureKind FailureKind,
  string Diagnostic);
