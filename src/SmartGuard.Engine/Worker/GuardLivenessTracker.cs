using SmartGuard.Contracts;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

internal enum GuardLivenessTransition { None, Entered, Recovered }

internal readonly record struct GuardLivenessObservation(
  GuardLivenessTransition Transition, TimeSpan Duration, string? LastFailureKind);

/// <summary>
/// Owns the guard liveness rule (GUARD-INV-LIVENESS). Called once per full round on the main round thread.
/// Stall = applicable rounds whose plan reads never succeeded for Threshold of monotonic time since the later
/// of the last success, the last wake or the last non-applicable round. A stall ends only with a successful
/// read; wakes and non-applicable rounds never end it.
/// </summary>
internal sealed class GuardLivenessTracker(TimeProvider time)
{
  internal static readonly TimeSpan Threshold = TimeSpan.FromMinutes(10);

  private long? _windowStart;
  private DateTimeOffset _windowStartUtc;
  private bool _stalled;
  private string? _lastFailureKind;

  internal GuardHealthStatus Current { get; private set; } = new(GuardHealthCodes.Healthy, null, null);

  internal GuardLivenessObservation Observe(bool applicable, bool planReadSucceeded, string? failureKind, bool resumed)
  {
    var now = time.GetTimestamp();
    var nowUtc = time.GetUtcNow();
    var transition = GuardLivenessTransition.None;
    var duration = TimeSpan.Zero;
    if (planReadSucceeded)
    {
      if (_stalled)
      {
        transition = GuardLivenessTransition.Recovered;
        duration = time.GetElapsedTime(_windowStart!.Value, now);
      }
      _stalled = false;
      _lastFailureKind = null;
      Restart(now, nowUtc);
    }
    else
    {
      _lastFailureKind = failureKind;
      if (_windowStart is null || (!_stalled && (resumed || !applicable)))
        Restart(now, nowUtc);
      else if (!_stalled && applicable && time.GetElapsedTime(_windowStart.Value, now) >= Threshold)
      {
        _stalled = true;
        transition = GuardLivenessTransition.Entered;
        duration = time.GetElapsedTime(_windowStart.Value, now);
      }
    }
    Current = !applicable ? new(GuardHealthCodes.NotApplicable, null, null)
      : _stalled ? new(GuardHealthCodes.Stalled, _windowStartUtc, _lastFailureKind)
      : new(GuardHealthCodes.Healthy, null, null);
    return new(transition, duration, _lastFailureKind);
  }

  private void Restart(long now, DateTimeOffset nowUtc)
  {
    _windowStart = now;
    _windowStartUtc = nowUtc;
  }

  internal static string FailureKindCode(PowerPlanFailureKind kind) => kind switch
  {
    PowerPlanFailureKind.ApiFailed => GuardHealthCodes.FailureKinds.ApiError,
    PowerPlanFailureKind.CallPending => GuardHealthCodes.FailureKinds.CallPending,
    PowerPlanFailureKind.TimedOut => GuardHealthCodes.FailureKinds.TimedOut,
    _ => GuardHealthCodes.FailureKinds.Other,
  };
}
