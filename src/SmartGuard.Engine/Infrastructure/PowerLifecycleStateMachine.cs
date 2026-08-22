namespace SmartGuard.Engine.Infrastructure;

internal sealed class PowerLifecycleStateMachine
{
  private const int TransitionQueueCapacity = 8;
  private static readonly TimeSpan DefaultRecoveryTimeout = TimeSpan.FromSeconds(30);

  private readonly object _gate = new();
  private readonly TimeProvider _timeProvider;
  private readonly TimeSpan _resumeStabilization;
  private readonly TimeSpan _recoveryTimeout;
  private readonly Queue<PowerLifecycleSignal> _transitions = new();

  private PowerLifecyclePhase _phase = PowerLifecyclePhase.Running;
  private long? _suspendStartedAt;
  private long? _resumeStartedAt;
  private bool _resetIdlePending;
  private bool _forcePowerRefreshPending;
  private bool _baselinePending;
  private bool _transitionOverflowPending;

  public PowerLifecycleStateMachine(
    TimeProvider? timeProvider = null,
    TimeSpan? resumeStabilization = null,
    TimeSpan? recoveryTimeout = null)
  {
    _timeProvider = timeProvider ?? TimeProvider.System;
    _resumeStabilization = resumeStabilization ?? TimeSpan.FromSeconds(5);
    _recoveryTimeout = recoveryTimeout ?? DefaultRecoveryTimeout;
    if (_resumeStabilization <= TimeSpan.Zero)
      throw new ArgumentOutOfRangeException(nameof(resumeStabilization));
    if (_recoveryTimeout <= TimeSpan.Zero)
      throw new ArgumentOutOfRangeException(nameof(recoveryTimeout));
  }

  public void Observe(PowerLifecycleSignal signal)
  {
    lock (_gate)
    {
      var now = _timeProvider.GetTimestamp();
      switch (signal.Kind)
      {
        case PowerLifecycleKind.Suspending:
          EnqueueTransition(signal);
          if (_phase != PowerLifecyclePhase.Suspending)
          {
            _phase = PowerLifecyclePhase.Suspending;
            _suspendStartedAt = now;
            _resumeStartedAt = null;
            _resetIdlePending = false;
            _forcePowerRefreshPending = false;
            _baselinePending = false;
          }
          break;

        case PowerLifecycleKind.Resumed:
          EnqueueTransition(signal);
          if (_phase == PowerLifecyclePhase.ResumeStabilizing)
          {
            _resumeStartedAt = now;
            _forcePowerRefreshPending = true;
            break;
          }

          _phase = PowerLifecyclePhase.ResumeStabilizing;
          _resumeStartedAt = now;
          _resetIdlePending = true;
          _baselinePending = true;
          _forcePowerRefreshPending = true;
          break;

        case PowerLifecycleKind.StatusChanged:
          _forcePowerRefreshPending = true;
          break;
      }
    }
  }

  public PowerLifecycleDirective TakeDirective()
  {
    lock (_gate)
    {
      var now = _timeProvider.GetTimestamp();
      var recoveryForced = false;

      if (_phase == PowerLifecyclePhase.Suspending &&
          HasElapsed(_suspendStartedAt, now, _recoveryTimeout))
      {
        _phase = PowerLifecyclePhase.ResumeStabilizing;
        _resumeStartedAt = now;
        _resetIdlePending = true;
        _baselinePending = true;
        _forcePowerRefreshPending = true;
        recoveryForced = true;
      }

      if (_phase == PowerLifecyclePhase.ResumeStabilizing &&
          HasElapsed(_resumeStartedAt, now, _resumeStabilization))
      {
        _phase = PowerLifecyclePhase.Running;
      }

      var transitions = _transitions.ToArray();
      _transitions.Clear();
      var resetIdleEpoch = _resetIdlePending;
      var forcePowerRefresh = _forcePowerRefreshPending;
      var absorbCurrentPlanAsBaseline = _baselinePending;
      var transitionOverflowed = _transitionOverflowPending;
      _resetIdlePending = false;
      _forcePowerRefreshPending = false;
      _baselinePending = false;
      _transitionOverflowPending = false;

      var runIteration = _phase != PowerLifecyclePhase.Suspending;
      var allowPolicyMutation = _phase == PowerLifecyclePhase.Running;
      var reevaluateAfter = ResolveReevaluation(now);

      return new PowerLifecycleDirective(
        _phase,
        runIteration,
        allowPolicyMutation,
        resetIdleEpoch,
        forcePowerRefresh,
        absorbCurrentPlanAsBaseline,
        transitions,
        recoveryForced,
        transitionOverflowed,
        reevaluateAfter);
    }
  }

  private void EnqueueTransition(PowerLifecycleSignal signal)
  {
    if (_transitions.Count > 0 && _transitions.Last().Kind == signal.Kind)
      return;

    if (_transitions.Count >= TransitionQueueCapacity)
    {
      _transitions.Dequeue();
      _transitionOverflowPending = true;
    }

    _transitions.Enqueue(signal);
  }

  private TimeSpan? ResolveReevaluation(long now)
  {
    if (_phase == PowerLifecyclePhase.Suspending)
      return Remaining(_suspendStartedAt, now, _recoveryTimeout);
    if (_phase == PowerLifecyclePhase.ResumeStabilizing)
      return Remaining(_resumeStartedAt, now, _resumeStabilization);
    return null;
  }

  private bool HasElapsed(long? startedAt, long now, TimeSpan duration)
    => startedAt.HasValue && _timeProvider.GetElapsedTime(startedAt.Value, now) >= duration;

  private TimeSpan Remaining(long? startedAt, long now, TimeSpan duration)
  {
    if (!startedAt.HasValue)
      return duration;

    var elapsed = _timeProvider.GetElapsedTime(startedAt.Value, now);
    return elapsed >= duration ? TimeSpan.FromMilliseconds(1) : duration - elapsed;
  }
}
