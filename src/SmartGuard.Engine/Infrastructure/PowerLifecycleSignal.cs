namespace SmartGuard.Engine.Infrastructure;

internal enum PowerLifecycleKind
{
  StatusChanged,
  Suspending,
  Resumed,
}

internal readonly record struct PowerLifecycleSignal(
  PowerLifecycleKind Kind,
  DateTimeOffset ObservedAtUtc);

internal enum PowerLifecyclePhase
{
  Running,
  Suspending,
  ResumeStabilizing,
}

internal readonly record struct PowerLifecycleDirective(
  PowerLifecyclePhase Phase,
  bool RunIteration,
  bool AllowPolicyMutation,
  bool ResetIdleEpoch,
  bool ForcePowerRefresh,
  bool AbsorbCurrentPlanAsBaseline,
  IReadOnlyList<PowerLifecycleSignal> TransitionsToLog,
  bool RecoveryForced,
  bool TransitionOverflowed,
  TimeSpan? ReevaluateAfter);
