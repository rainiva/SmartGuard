using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

[Collection("PowerPlanCatalogProvider")]
public class PowerSchemeServiceTests
{
  private static readonly Guid High = PowerPlanCatalogProvider.HighPerformancePlanGuid;

  [Fact]
  public async Task Successful_call_returns_its_value()
  {
    var native = new FakePowrProfNative { GetActive = () => (0, High) };
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      var result = await PowerSchemeService.GetActiveAsync();
      result.Succeeded.Should().BeTrue();
      result.Value.Should().Be(High);
      PowerSchemeService.LastSucceededAt.Should().NotBeNull();
    });
    native.Calls.Should().Equal("get-active");
  }

  [Fact]
  public async Task Nonzero_return_code_is_an_api_error_with_its_win32_code()
  {
    var native = new FakePowrProfNative { ReadName = _ => (2, string.Empty) };
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      var result = await PowerSchemeService.ReadNameAsync(Guid.NewGuid());
      result.Status.Should().Be(new PowerSchemeStatus("api-error", 2, result.Status.ElapsedMilliseconds));
    });
  }

  [Fact]
  public async Task Exception_from_the_native_layer_is_an_api_error_without_a_code()
  {
    var native = new FakePowrProfNative { GetActive = () => throw new DllNotFoundException("powrprof") };
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      var result = await PowerSchemeService.GetActiveAsync();
      result.Status.FailureKind.Should().Be("api-error");
      result.Status.Win32Error.Should().BeNull();
      PowerSchemeService.IsCallInFlight.Should().BeFalse("a faulted call released the gate");
    });
  }

  [Fact]
  public async Task Call_past_its_deadline_times_out_and_blocks_new_calls_until_it_returns()
  {
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var first = true;
    var native = new FakePowrProfNative
    {
      GetActive = () =>
      {
        if (first) { first = false; entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); }
        return (0, High);
      },
    };
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      try
      {
        var timedOut = await PowerSchemeService.GetActiveAsync();
        timedOut.Status.FailureKind.Should().Be("timed-out");
        entered.IsSet.Should().BeTrue();
        PowerSchemeService.IsCallInFlight.Should().BeTrue();
        PowerSchemeService.InFlightSince.Should().NotBeNull();

        var pending = await PowerSchemeService.ReadNameAsync(High);
        pending.Status.FailureKind.Should().Be("call-pending");
        native.Calls.Should().Equal(["get-active"], "a refused call never reaches the native layer");
      }
      finally { release.Set(); }

      await PowerSchemeTestScope.WaitUntilIdleAsync();
      PowerSchemeService.IsCallInFlight.Should().BeFalse();
      (await PowerSchemeService.GetActiveAsync()).Succeeded.Should().BeTrue("the returned call reopened the gate");
    }, deadline: TimeSpan.FromMilliseconds(100));
  }

  [Fact]
  public async Task Caller_cancellation_after_entry_stops_waiting_but_keeps_the_call_in_flight()
  {
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var native = new FakePowrProfNative
    {
      Enumerate = () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); return (0, []); },
    };
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      try
      {
        using var cancellation = new CancellationTokenSource();
        var call = PowerSchemeService.EnumerateSchemesAsync(cancellation.Token);
        entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        PowerSchemeService.IsCallInFlight.Should().BeTrue("a native call that already started keeps the gate");
        (await PowerSchemeService.GetActiveAsync()).Status.FailureKind.Should().Be("call-pending");
      }
      finally { release.Set(); }
    });
  }

  [Fact]
  public async Task Cancelled_while_queued_never_reaches_the_native_layer()
  {
    var scheduler = new ManualTaskScheduler();
    var native = new FakePowrProfNative();
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      using var cancellation = new CancellationTokenSource();
      var call = PowerSchemeService.SetActiveAsync(High, cancellationToken: cancellation.Token);
      cancellation.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
      PowerSchemeService.IsCallInFlight.Should().BeTrue("the queued work item still owns the gate");

      scheduler.RunPending().Should().Be(1);
      native.Calls.Should().BeEmpty("a caller that already observed cancellation never starts a write");
      PowerSchemeService.IsCallInFlight.Should().BeFalse("the skipped work item released the gate");
    }, scheduler: scheduler);
  }

  [Fact]
  public async Task Deadline_passed_while_queued_never_reaches_the_native_layer()
  {
    var scheduler = new ManualTaskScheduler();
    var native = new FakePowrProfNative();
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      var result = await PowerSchemeService.WriteValueIndexAsync(High, PowerSchemeSetting.VideoNormalLevel, PowerSource.Ac, 40);
      result.Status.FailureKind.Should().Be("timed-out");

      scheduler.RunPending().Should().Be(1);
      native.Calls.Should().BeEmpty();
      PowerSchemeService.IsCallInFlight.Should().BeFalse();
    }, deadline: TimeSpan.FromMilliseconds(50), scheduler: scheduler);
  }

  [Fact]
  public async Task Admission_is_rechecked_when_the_work_item_starts()
  {
    var scheduler = new ManualTaskScheduler();
    var native = new FakePowrProfNative();
    var permitted = true;
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      var call = PowerSchemeService.SetActiveAsync(High, admit: () => permitted);
      permitted = false; // lifecycle revoked after the caller's own check, before the native call
      scheduler.RunPending().Should().Be(1);

      (await call).Status.FailureKind.Should().Be("not-admitted");
      native.Calls.Should().BeEmpty();
      PowerSchemeService.IsCallInFlight.Should().BeFalse();
    }, scheduler: scheduler);
  }

  [Fact]
  public async Task Completed_call_releases_the_gate_before_its_result_is_visible()
  {
    var native = new FakePowrProfNative();
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      for (var i = 0; i < 5000; i++)
      {
        var result = await PowerSchemeService.GetActiveAsync();
        result.Status.FailureKind.Should().Be("none", $"sequential call {i} must not be refused by its own predecessor");
        PowerSchemeService.IsCallInFlight.Should().BeFalse($"the gate is free once call {i} is visible");
      }
    });
  }

  [Fact]
  public void Real_writes_are_refused_before_the_native_layer_when_disabled()
  {
    var previous = PowerSchemeService.RealWritesAllowed;
    PowerSchemeService.RealWritesAllowed = false;
    try
    {
      Assert.Throws<InvalidOperationException>(() => { _ = PowerSchemeService.SetActiveAsync(High); });
      Assert.Throws<InvalidOperationException>(() =>
      {
        _ = PowerSchemeService.WriteValueIndexAsync(High, PowerSchemeSetting.VideoNormalLevel, PowerSource.Ac, 40);
      });
    }
    finally { PowerSchemeService.RealWritesAllowed = previous; }
  }

  [Fact]
  public async Task Value_index_operations_target_only_the_fixed_video_settings()
  {
    var native = new FakePowrProfNative();
    await PowerSchemeTestScope.WithNative(native, async () =>
    {
      (await PowerSchemeService.WriteValueIndexAsync(High, PowerSchemeSetting.VideoNormalLevel, PowerSource.Dc, 40)).Succeeded.Should().BeTrue();
      (await PowerSchemeService.ReadValueIndexAsync(High, PowerSchemeSetting.AdaptiveBrightness, PowerSource.Ac)).Value.Should().Be(50u);
    });
    native.Calls.Should().Equal(
      $"write-value {High:D} {PowerSchemeService.VideoSubgroupGuid:D} {PowerSchemeService.VideoNormalLevelGuid:D} Dc 40",
      $"read-value {High:D} {PowerSchemeService.VideoSubgroupGuid:D} {PowerSchemeService.AdaptiveBrightnessGuid:D} Ac");
  }

  [Fact]
  public void Deadlines_are_bounded()
  {
    PowerSchemeService.DefaultDeadline.Should().Be(TimeSpan.FromSeconds(2));
    PowerSchemeService.EnumerateDeadline.Should().Be(TimeSpan.FromSeconds(5));
  }
}
