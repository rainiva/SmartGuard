using System.Runtime.ExceptionServices;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.Tests;

internal static class PowerSchemeTestScope
{
  private static readonly TimeSpan ConvergenceTimeout = TimeSpan.FromSeconds(10);

  private static readonly PowerCfgCommandResult Blocked =
    new(false, false, null, "", "powercfg is blocked inside PowerSchemeTestScope") { FailureKind = "start-failed" };

  // Synchronous on purpose: WPF tests run on the STA thread; loads started inside inherit the
  // AsyncLocal executor through the execution context. Blocking the STA thread here is safe: loads use
  // ConfigureAwait(false) and never need the dispatcher.
  // Before the global hooks are reset, every load must have published (the load gate is free) and no
  // native call may be in flight. Otherwise the test fails and the hooks stay in place, so a late load
  // can neither reach powercfg.exe nor publish into the next test's cleared cache.
  internal static void WithNativeSync(IPowrProfNative native, Action run)
  {
    PowerSchemeService.UseExecutorForTests(new PowerSchemeExecutor(native));
    PowerCfgProcessRunner.RunOverrideForTests = (_, _) => Blocked;
    PowerCfgProcessRunner.RunAsyncOverrideForTests = (_, _, _) => Task.FromResult(Blocked);
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    ExceptionDispatchInfo? failure = null;
    try { run(); }
    catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }

    var converged = PowerPlanCatalogProvider.WhenLoadsIdleForTests().Wait(ConvergenceTimeout)
      && SpinWait.SpinUntil(() => !PowerSchemeService.IsCallInFlight, ConvergenceTimeout);
    PowerSchemeService.UseExecutorForTests(null);
    if (converged)
    {
      PowerCfgProcessRunner.RunOverrideForTests = null;
      PowerCfgProcessRunner.RunAsyncOverrideForTests = null;
      PowerPlanCatalogProvider.ClearSessionCacheForTests();
    }

    failure?.Throw();
    if (!converged)
      throw new TimeoutException(
        $"A catalog load or scheme-service call was still running after {ConvergenceTimeout.TotalSeconds:0} s; the global test hooks were left in place.");
  }
}
