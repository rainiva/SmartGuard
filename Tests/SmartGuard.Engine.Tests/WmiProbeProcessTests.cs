using SmartGuard.Engine.Infrastructure;
using SmartGuard.Configuration;
using Xunit;

namespace SmartGuard.Engine.Tests;

public class WmiProbeProcessTests
{
  [Fact]
  public void Wmi_process_boundary_exists()
  {
    Assert.NotNull(typeof(BrightnessService).Assembly.GetType("SmartGuard.Engine.Infrastructure.WmiProbeProcess"));
  }

  [Fact]
  public void Pre_cancelled_probe_never_starts()
  {
    var calls = 0;
    var probe = new WmiProbeProcess((_, _, _, _) => { calls++; return Task.FromResult(Reply("SGWMI1|brightness|50")); });
    using var stop = new CancellationTokenSource();
    stop.Cancel();
    Assert.Equal(-1, probe.ReadBrightness(stop.Token));
    Assert.Equal(0, calls);
  }

  [Fact]
  public async Task Busy_probe_has_zero_queue_and_ignores_invalid_reply()
  {
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var finish = new TaskCompletionSource<PowerCfgCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var calls = 0;
    var probe = new WmiProbeProcess((_, _, _, _) => { Interlocked.Increment(ref calls); entered.SetResult(); return finish.Task; });
    var first = Task.Run(() => probe.ReadBrightness());
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.Equal(-1, probe.ReadBrightness());
    Assert.Equal(1, calls);
    finish.SetResult(Reply("SGWMI1|brightness|101"));
    Assert.Equal(-1, await first);
  }

  [Theory]
  [InlineData("SGWMI1|brightness|50")]
  [InlineData("garbage")]
  public void Only_valid_versioned_reply_is_accepted(string reply)
  {
    var probe = new WmiProbeProcess((_, _, _, _) => Task.FromResult(Reply(reply)));
    Assert.Equal(reply.StartsWith("SGWMI1") ? 50 : -1, probe.ReadBrightness());
  }

  private static PowerCfgCommandResult Reply(string text) => new(true, false, 0, text, "");

  [Fact]
  public void Battery_success_does_not_recover_a_brightness_failure()
  {
    var clock = new Clock();
    var events = new List<WmiProbeObservation>();
    var first = true;
    var probe = new WmiProbeProcess((_, args, _, _) =>
    {
      if (first) { first = false; return Task.FromResult(Reply("invalid")); }
      return Task.FromResult(Reply(args.Contains("battery-read") ? "SGWMI1|battery|80|ac" : "SGWMI1|brightness|50"));
    }, clock, events.Add);
    probe.ReadBrightness();
    clock.Advance(TimeSpan.FromSeconds(30));
    Assert.NotNull(probe.ReadBattery());
    Assert.Single(events);
    Assert.Equal(50, probe.ReadBrightness());
    Assert.Equal("brightness-read", events[1].Operation);
    Assert.True(events[1].Recovered);
  }

  [Fact]
  public async Task Observer_does_not_hold_process_ownership_lock()
  {
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var probe = new WmiProbeProcess((_, _, _, _) => Task.FromResult(Reply("invalid")), observe: _ =>
    {
      entered.Set();
      Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
    });
    var first = Task.Run(() => probe.ReadBrightness());
    try
    {
      Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
      Assert.Equal(-1, await Task.Run(() => probe.ReadBrightness()).WaitAsync(TimeSpan.FromSeconds(1)));
    }
    finally { release.Set(); }
    Assert.Equal(-1, await first);
  }

  [Fact]
  public void Cancellation_during_execution_discards_even_a_successful_late_reply()
  {
    using var stop = new CancellationTokenSource();
    var events = new List<WmiProbeObservation>();
    var probe = new WmiProbeProcess((_, _, _, _) =>
    {
      stop.Cancel();
      return Task.FromResult(Reply("SGWMI1|brightness|42"));
    }, observe: events.Add);
    Assert.Equal(-1, probe.ReadBrightness(stop.Token));
    Assert.Equal("cancelled", Assert.Single(events).FailureKind);
    Assert.False(events[0].Recovered);
  }

  [Fact]
  public async Task Cancellation_accepts_cleanup_certificate_without_accepting_late_business_reply()
  {
    using var stop = new CancellationTokenSource();
    var events = new List<WmiProbeObservation>();
    var probe = new WmiProbeProcess(async (_, args, _, token) =>
    {
      if (args.Contains("battery-read")) return Reply("SGWMI1|battery|5|battery");
      stop.Cancel();
      await Task.Delay(100);
      return Reply("SGWMI1|brightness|50");
    }, observe: events.Add);
    Assert.Equal(-1, await Task.Run(() => probe.ReadBrightness(stop.Token)));
    Assert.Equal((5, (bool?)false), probe.ReadBattery());
    Assert.True(Assert.Single(events).TerminationConfirmed);
    Assert.InRange(events[0].ElapsedMilliseconds, 50, 1000);
  }

  [Fact]
  public void Sustained_failure_reports_bounded_summary_and_retains_skipped_count()
  {
    var clock = new Clock();
    var events = new List<WmiProbeObservation>();
    var probe = new WmiProbeProcess((_, _, _, _) => Task.FromResult(
      new PowerCfgCommandResult(true, true, null, "", "")
      { FailureKind = "cleanup-unconfirmed", TerminationConfirmed = false }), clock, events.Add);
    probe.ReadBrightness();
    for (var i = 0; i < 100; i++) probe.ReadBrightness();
    Assert.Single(events);
    clock.Advance(TimeSpan.FromSeconds(60));
    probe.ReadBrightness();
    Assert.Equal(2, events.Count);
    Assert.Equal(101, events[1].SuppressedCount);
    Assert.Equal("cleanup-unconfirmed", events[1].FailureKind);
  }

  [Fact]
  public void Failure_cools_down_then_first_success_reports_recovery_and_cumulative_suppression()
  {
    var clock = new Clock();
    var events = new List<WmiProbeObservation>();
    var calls = 0;
    var probe = new WmiProbeProcess((_, _, budget, _) =>
    {
      Assert.Equal(TimeSpan.FromSeconds(2), budget);
      return Task.FromResult(++calls == 1
        ? new PowerCfgCommandResult(true, true, null, "", "") { FailureKind = "timed-out" }
        : Reply("SGWMI1|brightness|42"));
    }, clock, events.Add);
    Assert.Equal(-1, probe.ReadBrightness());
    Assert.Equal(-1, probe.ReadBrightness());
    Assert.Single(events);
    Assert.Equal(1, calls);
    clock.Advance(TimeSpan.FromSeconds(30));
    Assert.Equal(42, probe.ReadBrightness());
    Assert.Equal(2, events.Count);
    Assert.True(events[1].Recovered);
    Assert.Equal(1, events[1].SuppressedCount);
  }

  [Fact]
  public void Unconfirmed_cleanup_permanently_closes_spawning()
  {
    var clock = new Clock();
    var calls = 0;
    var probe = new WmiProbeProcess((_, _, _, _) =>
    {
      calls++;
      return Task.FromResult(new PowerCfgCommandResult(true, true, null, "", "")
        { FailureKind = "cleanup-unconfirmed", TerminationConfirmed = false });
    }, clock);
    Assert.Equal(-1, probe.ReadBrightness());
    clock.Advance(TimeSpan.FromDays(1));
    Assert.Null(probe.ReadBattery());
    probe.SetBrightness(50);
    Assert.Equal(1, calls);
  }

  [Fact]
  public async Task Nonreturning_executor_is_bounded_and_late_result_cannot_reopen_boundary()
  {
    var late = new TaskCompletionSource<PowerCfgCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var events = new List<WmiProbeObservation>();
    var calls = 0;
    var probe = new WmiProbeProcess((_, _, _, _) => { calls++; return late.Task; }, observe: events.Add);
    var result = await Task.Run(() => probe.ReadBrightness()).WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(-1, result);
    Assert.Equal("cleanup-unconfirmed", Assert.Single(events).FailureKind);
    late.SetResult(Reply("SGWMI1|brightness|44"));
    Assert.Equal(-1, probe.ReadBrightness());
    Assert.Equal(1, calls);
  }

  [Fact]
  public void Oversized_or_non_ascii_reply_is_rejected()
  {
    foreach (var text in new[] { "SGWMI1|brightness|" + new string('0', 200) + "1", "SGWMI1|brightness|５０" })
    {
      var probe = new WmiProbeProcess((_, _, _, _) => Task.FromResult(Reply(text)));
      Assert.Equal(-1, probe.ReadBrightness());
    }
  }

  [Theory]
  [InlineData("SGWMI1|battery|75|ac", true)]
  [InlineData("SGWMI1|battery|75|battery", false)]
  public void Battery_protocol_preserves_ac_hint(string text, bool onAc)
  {
    var probe = new WmiProbeProcess((_, _, _, _) => Task.FromResult(Reply(text)));
    Assert.Equal((75, (bool?)onAc), probe.ReadBattery());
  }

  [Fact]
  public void Operation_failure_does_not_cool_other_operations()
  {
    var probe = new WmiProbeProcess((_, args, _, _) => Task.FromResult(Reply(
      args.Contains("battery-read") ? "SGWMI1|battery|5|battery" : "invalid")));
    Assert.Equal(-1, probe.ReadBrightness());
    Assert.Equal((5, (bool?)false), probe.ReadBattery());
  }

  [Theory]
  [InlineData("unavailable")]
  [InlineData("wmi-failed")]
  [InlineData("provider-failed")]
  [InlineData("write-failed")]
  [InlineData("secret-path", "child-failed")]
  public void Child_error_protocol_preserves_only_safe_codes(string code, string? expected = null)
  {
    var events = new List<WmiProbeObservation>();
    var probe = new WmiProbeProcess((_, _, _, _) => Task.FromResult(
      new PowerCfgCommandResult(false, false, 3, "SGWMI1|error|" + code, "")), observe: events.Add);
    Assert.Equal(-1, probe.ReadBrightness());
    Assert.Equal(expected ?? code, Assert.Single(events).FailureKind);
  }

  [Fact]
  public void Suppression_counts_are_per_operation_interval_and_fault_cycle()
  {
    var clock = new Clock();
    var events = new List<WmiProbeObservation>();
    var fail = true;
    var probe = new WmiProbeProcess((_, args, _, _) => Task.FromResult(Reply(
      args.Contains("battery-read") ? "SGWMI1|battery|5|battery" : fail ? "invalid" : "SGWMI1|brightness|50")), clock, events.Add);
    probe.ReadBrightness();
    probe.ReadBrightness();
    probe.ReadBattery();
    clock.Advance(TimeSpan.FromSeconds(30));
    probe.ReadBrightness(); // a real repeat failure also belongs to the suppressed interval
    clock.Advance(TimeSpan.FromSeconds(30));
    probe.ReadBrightness();
    Assert.Equal(2, events[1].SuppressedCount);
    fail = false;
    clock.Advance(TimeSpan.FromSeconds(30));
    probe.ReadBrightness();
    Assert.Equal(0, events[2].SuppressedCount);
    fail = true;
    probe.ReadBrightness();
    Assert.Equal(0, events[3].SuppressedCount);
  }

  private sealed class Clock : TimeProvider
  {
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    internal void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
  }
}
