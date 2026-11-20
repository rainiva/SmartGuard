using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

[Collection("PowerPlanCatalogProvider")]
public class PowerPlanActivatorTests
{
  private static readonly Guid High = PowerPlanCatalogProvider.HighPerformancePlanGuid;

  [Fact]
  public async Task Activation_switches_through_the_scheme_service()
  {
    var native = new FakePowrProfNative();
    PowerSchemeStatus status = default;
    await PowerSchemeTestScope.WithNative(native, () =>
    {
      status = new PowerPlanActivator().SetActivePlan(High);
      return Task.CompletedTask;
    });
    status.Succeeded.Should().BeTrue();
    native.Calls.Should().Equal($"set-active {High:D}");
  }

  [Fact]
  public async Task Activation_returns_within_a_bound_when_the_switch_hangs()
  {
    using var started = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    // The native call blocks; the executor's deadline must bound the wait.
    var native = new FakePowrProfNative
    {
      SetActive = _ =>
      {
        started.Set();
        release.Wait(TimeSpan.FromSeconds(30));
        return 0;
      },
    };
    PowerSchemeStatus status = default;
    Task? call = null;
    var entered = false;
    var finished = false;
    await PowerSchemeTestScope.WithNative(native, () =>
    {
      try
      {
        call = Task.Run(() => status = new PowerPlanActivator().SetActivePlan(High));
        // The bound is measured from the switch itself, not from when the thread pool ran the task.
        entered = started.Wait(TimeSpan.FromSeconds(10));
        finished = entered && call.Wait(TimeSpan.FromSeconds(4));
      }
      finally { release.Set(); }
      return Task.CompletedTask;
    }, deadline: TimeSpan.FromMilliseconds(100));
    call!.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("the call under test must finish before the test ends");
    entered.Should().BeTrue("the switch under test must have started");
    finished.Should().BeTrue("the tray must never wait without a bound for a power switch");
    status.FailureKind.Should().Be("timed-out");
  }
}
