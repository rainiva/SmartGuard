using System.Diagnostics;
using SmartGuard.Configuration;

namespace SmartGuard.Engine.Tests;

internal static class PowerSchemeTestScope
{
  // Blocks the legacy powercfg runner for the whole scope: a RED that still falls into the old
  // implementation fails instead of starting powercfg.exe and changing this machine's settings.
  private static readonly PowerCfgCommandResult Blocked =
    new(false, false, null, "", "powercfg is blocked inside PowerSchemeTestScope") { FailureKind = "start-failed" };

  internal static async Task WithNative(IPowrProfNative native, Func<Task> run, TimeSpan? deadline = null)
  {
    Enter(native, deadline);
    try { await run(); }
    finally
    {
      try { await WaitUntilIdleAsync(); }
      finally { Exit(); }
    }
  }

  internal static void WithNativeSync(IPowrProfNative native, Action run)
  {
    Enter(native, null);
    try { run(); }
    finally
    {
      try { WaitUntilIdleAsync().GetAwaiter().GetResult(); }
      finally { Exit(); }
    }
  }

  internal static async Task WaitUntilIdleAsync()
  {
    var watch = Stopwatch.StartNew();
    while (PowerSchemeService.IsCallInFlight)
    {
      if (watch.Elapsed > TimeSpan.FromSeconds(10))
        throw new TimeoutException("A fake native call is still in flight; the test left the gate held.");
      await Task.Delay(10);
    }
  }

  // Enter/Exit are synchronous so the AsyncLocal executor change applies to the caller's flow.
  private static void Enter(IPowrProfNative native, TimeSpan? deadline)
  {
    PowerSchemeService.UseExecutorForTests(new PowerSchemeExecutor(native, deadline));
    PowerCfgProcessRunner.RunOverrideForTests = (_, _) => Blocked;
    PowerCfgProcessRunner.RunAsyncOverrideForTests = (_, _, _) => Task.FromResult(Blocked);
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
  }

  private static void Exit()
  {
    PowerSchemeService.UseExecutorForTests(null);
    PowerCfgProcessRunner.RunOverrideForTests = null;
    PowerCfgProcessRunner.RunAsyncOverrideForTests = null;
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
  }
}
