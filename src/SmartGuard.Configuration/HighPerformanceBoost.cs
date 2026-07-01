namespace SmartGuard.Configuration;

public static class HighPerformanceBoost
{
  public static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(1);

  public static void Apply(
    GuardConfigRepository repository,
    string root,
    IPowerPlanActivator activator,
    TimeSpan? duration = null)
  {
    var config = repository.LoadOrDefault(root);
    var until = DateTime.Now.Add(duration ?? DefaultDuration);
    repository.SetManualHighPerformanceUntil(until);
    activator.SetActivePlan(config.ActivePlanGuid);
    repository.AppendInfoLog(
      ManualHighPerformanceMessages.FormatApplied(until),
      SmartGuardPaths.StartupLogFile(root));
  }
}
