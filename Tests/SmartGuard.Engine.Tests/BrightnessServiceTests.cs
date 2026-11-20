using SmartGuard.Configuration;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class BrightnessServiceTests
{
  [Theory]
  [InlineData(2, -1)]
  [InlineData(int.MaxValue, 0)]
  [InlineData(2, int.MaxValue)]
  [InlineData(0, 0)]
  public async Task Invalid_retry_parameters_fail_before_any_hardware_call(int attempts, int delay)
  {
    var calls = 0;
    var events = new List<WmiProbeObservation>();
    var service = new BrightnessService(new WmiProbeProcess((_, _, _, _) =>
    { calls++; return Task.FromResult(new PowerCfgCommandResult(true, false, 0, "SGWMI1|write|ok", "")); }, observe: events.Add));
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    Assert.Equal(-1, await Task.Run(() => service.RestoreWithRetryAsync(50, attempts, delay, stop.Token)).WaitAsync(TimeSpan.FromSeconds(1)));
    Assert.Equal(0, calls);
    Assert.Equal("invalid-parameter", Assert.Single(events).FailureKind);
    Assert.Equal("brightness-restore", events[0].Operation);
  }

  [Fact]
  public async Task Restore_total_budget_stops_further_attempts_and_reports_degradation()
  {
    var calls = 0;
    var events = new List<WmiProbeObservation>();
    var service = new BrightnessService(new WmiProbeProcess((_, args, _, _) =>
    { calls++; return Task.FromResult(new PowerCfgCommandResult(true, false, 0,
        args.Contains("brightness-write") ? "SGWMI1|write|ok" : "SGWMI1|brightness|10", "")); }, observe: events.Add));
    Assert.Equal(-1, await service.RestoreWithRetryAsync(50, 5, 1000).WaitAsync(TimeSpan.FromSeconds(4)));
    Assert.InRange(calls, 1, 6);
    // Deadline delivery can race a completed child reply. Its cancellation
    // record is valid evidence alongside the single restore-budget record.
    var restore = Assert.Single(events, e => e.Operation == "brightness-restore");
    Assert.Equal("budget-exhausted", restore.FailureKind);
    Assert.Equal(3000, restore.BudgetMilliseconds);
    var cancelled = events.Where(e => e.Operation != "brightness-restore").ToArray();
    Assert.InRange(cancelled.Length, 0, 1);
    Assert.All(cancelled, e =>
    {
      Assert.Contains(e.Operation, new[] { "brightness-read", "brightness-write" });
      Assert.Equal("cancelled", e.FailureKind);
    });
  }

  [Fact]
  public async Task Exhausted_attempts_preserve_last_observed_brightness_and_report_nonconvergence()
  {
    var events = new List<WmiProbeObservation>();
    var service = new BrightnessService(new WmiProbeProcess((_, args, _, _) => Task.FromResult(
      new PowerCfgCommandResult(true, false, 0, args.Contains("brightness-write")
        ? "SGWMI1|write|ok" : "SGWMI1|brightness|40", "")), observe: events.Add));
    Assert.Equal(40, await service.RestoreWithRetryAsync(50, 2, 0));
    Assert.Equal("restore-not-converged", Assert.Single(events).FailureKind);
  }

  [Fact]
  public async Task Restore_budget_also_bounds_an_executor_that_ignores_cancellation()
  {
    var calls = 0;
    var events = new List<WmiProbeObservation>();
    var never = new TaskCompletionSource<PowerCfgCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var service = new BrightnessService(new WmiProbeProcess(async (_, _, _, _) =>
    {
      if (Interlocked.Increment(ref calls) != 1) return await never.Task;
      await Task.Delay(1500);
      return new PowerCfgCommandResult(true, false, 0, "SGWMI1|write|ok", "");
    }, observe: events.Add));
    Assert.Equal(-1, await Task.Run(() => service.RestoreWithRetryAsync(50, 5, 0)).WaitAsync(TimeSpan.FromMilliseconds(3700)));
    Assert.Equal(2, calls);
    Assert.Contains(events, e => e.Operation == "brightness-restore" && e.FailureKind == "budget-exhausted");
    Assert.False(events.Single(e => e.Operation == "brightness-restore").TerminationConfirmed);
  }

  [Fact]
  public async Task Unresponsive_brightness_provider_returns_unavailable_without_stalling_guardian()
  {
    // Exercise the real read-only child boundary as well as injected failures.
    var result = await Task.Run(() => new BrightnessService().GetBrightness())
      .WaitAsync(TimeSpan.FromSeconds(3));
    result.Should().BeInRange(-1, 100);
  }

  [Fact]
  public async Task Precancelled_restore_never_writes()
  {
    var calls = 0;
    var service = new BrightnessService(new WmiProbeProcess((_, _, _, _) =>
    { calls++; return Task.FromResult(new PowerCfgCommandResult(true, false, 0, "SGWMI1|write|ok", "")); }));
    using var stop = new CancellationTokenSource();
    stop.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RestoreWithRetryAsync(50, 2, 0, stop.Token));
    Assert.Equal(0, calls);
  }

  [Fact]
  public async Task Revoked_qualification_after_first_write_prevents_reads_and_further_writes()
  {
    var calls = 0;
    var permitted = true;
    var service = new BrightnessService(new WmiProbeProcess((_, arguments, _, _) =>
    {
      calls++;
      Assert.Equal("--wmi-probe brightness-write 50", arguments);
      permitted = false;
      return Task.FromResult(new PowerCfgCommandResult(true, false, 0, "SGWMI1|write|ok", ""));
    }));
    Assert.Equal(-1, await service.RestoreWithRetryAsync(50, 3, 0, canMutate: () => permitted));
    Assert.Equal(1, calls);
  }

  [Fact]
  public void Brightness_reads_return_after_the_unproven_child_is_proven()
  {
    var clock = new FakeClock();
    var released = false;
    var calls = 0;
    var probe = new WmiProbeProcess((_, _, _, _) => Task.FromResult(++calls == 1
      ? new PowerCfgCommandResult(true, true, null, "", "") { FailureKind = "cleanup-unconfirmed", TerminationConfirmed = false,
          PendingCleanup = UnconfirmedChild.ForTests(() => (released, released ? "confirmed" : "pipes-open"), "pipes-open") }
      : new PowerCfgCommandResult(true, false, 0, "SGWMI1|brightness|63", "")), clock);
    var brightness = new BrightnessService(probe);

    Assert.Equal(-1, brightness.GetBrightness());
    clock.Advance(TimeSpan.FromSeconds(31));
    Assert.Equal(-1, brightness.GetBrightness());             // still unproven: no new child
    Assert.Equal(1, calls);
    released = true;
    Assert.Equal(63, brightness.GetBrightness());             // proven: a new child reads the real level
    Assert.Equal(2, calls);
  }

  private sealed class FakeClock : TimeProvider
  {
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    internal void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
  }
}
