using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

internal readonly record struct GuardianIterationContext(
  bool AllowPolicyMutation,
  bool ResetIdleEpoch,
  bool ForcePowerRefresh,
  bool AbsorbCurrentPlanAsBaseline,
  PowerLifecyclePhase LifecyclePhase)
{
  public static GuardianIterationContext Running { get; } = new(
    true, false, false, false, PowerLifecyclePhase.Running);
}
