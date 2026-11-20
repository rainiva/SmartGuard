using System.Diagnostics;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

internal static class PowerSchemeTestScope
{
  private static readonly PowerCfgCommandResult Blocked =
    new(false, false, null, "", "powercfg is blocked inside PowerSchemeTestScope") { FailureKind = "start-failed" };

  // The executor override is AsyncLocal: it reaches only this test's async flow, so parallel
  // tests never share this fake, its gate or its health facts.
  internal static async Task WithNative(IPowrProfNative native, Func<Task> run, TimeSpan? deadline = null,
    TaskScheduler? scheduler = null)
  {
    PowerSchemeService.UseExecutorForTests(new PowerSchemeExecutor(native, deadline, scheduler));
    // RED may still fall into the legacy powercfg catalog path; block it instead of spawning powercfg.exe.
    PowerCfgProcessRunner.RunOverrideForTests = (_, _) => Blocked;
    PowerCfgProcessRunner.RunAsyncOverrideForTests = (_, _, _) => Task.FromResult(Blocked);
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    try { await run(); }
    finally
    {
      // A blocked fake must return before the scope ends: the gate belongs to the in-flight call.
      var idle = false;
      try
      {
        await WaitUntilIdleAsync();
        idle = true;
      }
      finally
      {
        // Reset in this frame (not in a nested async method) so the change applies to this flow.
        PowerSchemeService.UseExecutorForTests(null);
        PowerPlanCatalogProvider.ClearSessionCacheForTests();
        // A call still in flight could fall back to the legacy path; keep powercfg blocked for it.
        if (idle)
        {
          PowerCfgProcessRunner.RunOverrideForTests = null;
          PowerCfgProcessRunner.RunAsyncOverrideForTests = null;
        }
      }
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
}
