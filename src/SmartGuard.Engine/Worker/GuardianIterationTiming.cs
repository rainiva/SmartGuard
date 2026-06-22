using SmartGuard.Configuration;

namespace SmartGuard.Engine.Worker;

public static class GuardianIterationTiming
{
  public static int ResolveWaitSeconds(string configPath)
    => Math.Max(5, GuardConfig.LoadFromFile(configPath).CheckIntervalSec);
}
