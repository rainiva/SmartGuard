using System.Runtime.ExceptionServices;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.Tests;

internal static class PowerSchemeTestScope
{
  private static readonly TimeSpan ConvergenceTimeout = TimeSpan.FromSeconds(10);

  // Synchronous on purpose: WPF tests run on the STA thread; loads started inside inherit the
  // AsyncLocal executor through the execution context. Blocking the STA thread here is safe: loads use
  // ConfigureAwait(false) and never need the dispatcher.
  // Before the global hooks are reset, every load must have published (the load gate is free) and no
  // native call may be in flight. Otherwise the test fails and the session cache is left alone, so a
  // late load cannot publish into the next test's cleared cache.
  internal static void WithNativeSync(IPowrProfNative native, Action run)
  {
    PowerSchemeService.UseExecutorForTests(new PowerSchemeExecutor(native));
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    ExceptionDispatchInfo? failure = null;
    try { run(); }
    catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }

    var converged = PowerPlanCatalogProvider.WhenLoadsIdleForTests().Wait(ConvergenceTimeout)
      && SpinWait.SpinUntil(() => !PowerSchemeService.IsCallInFlight, ConvergenceTimeout);
    PowerSchemeService.UseExecutorForTests(null);
    if (converged)
      PowerPlanCatalogProvider.ClearSessionCacheForTests();

    failure?.Throw();
    if (!converged)
      throw new TimeoutException(
        $"A catalog load or scheme-service call was still running after {ConvergenceTimeout.TotalSeconds:0} s; the session cache was left as it was.");
  }
}
