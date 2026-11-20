using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;

namespace SmartGuard.Engine.Worker;

internal sealed partial class GuardianLoopIterationState
{
  private long? _foregroundExitAt;
  private bool _wasListed;
  private string? _observationFingerprint;
  private int _observationSeconds;
  public string? ForegroundObservationPhase { get; private set; }
  public string? LastListedProgram { get; private set; }
  public bool IsForegroundObserving => _foregroundExitAt is not null;

  // Only explanatory state: admission is always freshly decided by PolicyEngine.
  // Unknown identity cannot manufacture a confirmed exit or confer action permission.
  public void UpdateForegroundObservation(GuardConfigReadResult read, ForegroundAdmission admission,
    ForegroundObservation foreground, TimeProvider time)
  {
    if (!read.IsValid || read.Config is not { } config || !config.ForegroundRestrictionEnabled
      || config.Paused || config.IsManualHighPerformanceActive(time.GetLocalNow().LocalDateTime)
      || admission.Reason is GuardDecisionReason.ForegroundListEmpty or GuardDecisionReason.ForegroundProgramPathsInvalid)
    { ResetForegroundObservation(); return; }
    if (_observationFingerprint != read.RuleFingerprint || _observationSeconds != config.ForegroundObservationSec)
      ResetForegroundObservation();
    _observationFingerprint = read.RuleFingerprint;
    _observationSeconds = config.ForegroundObservationSec;
    if (admission.Reason == GuardDecisionReason.ForegroundMatched)
    {
      if (_wasListed && !string.Equals(LastListedProgram, Path.GetFileName(foreground.ExecutablePath), StringComparison.OrdinalIgnoreCase))
        ForegroundObservationPhase = null;
      if (ForegroundObservationPhase is "observing" or "expired") ForegroundObservationPhase = "returned";
      _foregroundExitAt = null;
      _wasListed = true;
      LastListedProgram = Path.GetFileName(foreground.ExecutablePath);
      return;
    }
    if (admission.Reason == GuardDecisionReason.WaitingForListedProgram && _wasListed)
    {
      _wasListed = false;
      _foregroundExitAt = time.GetTimestamp();
      ForegroundObservationPhase = "observing";
    }
    if (_foregroundExitAt is long start && time.GetElapsedTime(start) >= TimeSpan.FromSeconds(_observationSeconds))
    {
      _foregroundExitAt = null;
      ForegroundObservationPhase = "expired";
    }
  }

  public TimeSpan? ForegroundObservationRemaining(TimeProvider time)
    => _foregroundExitAt is long start
      ? TimeSpan.FromSeconds(_observationSeconds) - time.GetElapsedTime(start) : null;

  public void ResetForegroundObservation()
  {
    _foregroundExitAt = null;
    _wasListed = false;
    ForegroundObservationPhase = LastListedProgram = _observationFingerprint = null;
    _observationSeconds = 0;
  }
}
