namespace SmartGuard.Configuration;

public static class GuardianIterationTiming
{
  public static int ResolveWaitSeconds(GuardConfigRepository repository, string root)
    => ResolveWaitSeconds(repository, root, lifecycleReevaluateAfter: null);

  public static int ResolveWaitSeconds(
    GuardConfigRepository repository,
    string root,
    TimeSpan? lifecycleReevaluateAfter)
  {
    var configured = Math.Max(5, repository.LoadOrDefault(root).CheckIntervalSec);
    if (lifecycleReevaluateAfter is not TimeSpan remaining)
      return configured;

    var deadline = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
    return Math.Min(configured, deadline);
  }
}
