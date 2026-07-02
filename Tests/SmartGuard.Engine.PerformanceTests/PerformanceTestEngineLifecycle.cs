using SmartGuard.Configuration;

namespace SmartGuard.Engine.PerformanceTests;

internal static class PerformanceTestEngineLifecycle
{
  internal static void Stop(string repoRoot)
  {
    _ = repoRoot;
    EngineLifecycle.StopProcesses();
    Thread.Sleep(TimeSpan.FromSeconds(1));
  }
}
