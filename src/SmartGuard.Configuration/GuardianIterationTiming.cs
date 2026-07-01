namespace SmartGuard.Configuration;

public static class GuardianIterationTiming
{
  public static int ResolveWaitSeconds(GuardConfigRepository repository, string root)
    => Math.Max(5, repository.LoadOrDefault(root).CheckIntervalSec);
}
