namespace SmartGuard.Engine.Infrastructure;

internal enum PowerPlanFailureKind
{
  None,
  TimedOut,
  VerificationMismatch,
  ApiFailed,
  CallPending,
  NotAdmitted,
}

internal readonly record struct PowerPlanProbeResult(
  bool Succeeded,
  PowerSchemeInfo? Plan,
  PowerPlanFailureKind FailureKind,
  string Diagnostic,
  int? Win32Error = null)
{
  public Guid? ObservedGuid => Plan?.Guid;
}

internal readonly record struct PowerPlanApplyResult(
  bool Succeeded,
  Guid TargetGuid,
  Guid? ObservedGuid,
  PowerPlanFailureKind FailureKind,
  string Diagnostic,
  int? Win32Error = null);

internal enum BrightnessCapability
{
  Supported,
  Unsupported,
  ProbeFailed,
}
