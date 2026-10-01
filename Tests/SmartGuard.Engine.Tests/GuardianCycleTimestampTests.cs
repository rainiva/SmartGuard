using FluentAssertions;
using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

public class GuardianCycleTimestampTests
{
  [Theory]
  [InlineData(1000)]
  [InlineData(10_000_000)]
  [InlineData(1_000_000_000)]
  public async Task Periodic_deadline_is_fifteen_seconds_at_any_timestamp_frequency(long frequency)
  {
    using var rig = new Rig(frequency);
    await rig.Cycle();
    rig.Coordinator.ComputeWaitDelay().Should().Be(TimeSpan.FromSeconds(15));
    rig.Time.Advance(TimeSpan.FromSeconds(14));
    await rig.Cycle();
    rig.Full.Should().Be(1);
    rig.Time.Advance(TimeSpan.FromSeconds(1));
    await rig.Cycle();
    rig.Full.Should().Be(2);
  }

  [Theory]
  [InlineData(1000)]
  [InlineData(10_000_000)]
  [InlineData(1_000_000_000)]
  public async Task Last_foreground_signal_waits_one_real_second_then_is_consumed(long frequency)
  {
    using var rig = new Rig(frequency);
    await rig.Cycle();
    rig.Time.Advance(TimeSpan.FromMilliseconds(250));
    rig.Coordinator.MarkForegroundSignal();
    await rig.Cycle();
    rig.Reads.Should().Be(1);
    rig.Time.Advance(TimeSpan.FromMilliseconds(500));
    rig.Coordinator.MarkForegroundSignal();
    await rig.Cycle();
    rig.Reads.Should().Be(1);
    rig.Coordinator.HasPendingForegroundSignal.Should().BeTrue();
    rig.Coordinator.ComputeWaitDelay().Should().Be(TimeSpan.FromMilliseconds(500));
    rig.Time.Advance(TimeSpan.FromMilliseconds(500));
    await rig.Cycle();
    rig.Reads.Should().Be(2);
    rig.Coordinator.HasPendingForegroundSignal.Should().BeFalse();
  }

  [Theory]
  [InlineData(1000)]
  [InlineData(10_000_000)]
  [InlineData(1_000_000_000)]
  public async Task Admission_transition_waits_for_five_real_seconds_before_full_work(long frequency)
  {
    using var rig = new Rig(frequency);
    await rig.Cycle();
    rig.Time.Advance(TimeSpan.FromSeconds(1));
    rig.Admitted = true;
    rig.Coordinator.MarkForegroundSignal();
    await rig.Cycle();
    rig.Full.Should().Be(1);
    rig.Coordinator.ComputeWaitDelay().Should().Be(TimeSpan.FromSeconds(4));
    rig.Time.Advance(TimeSpan.FromSeconds(4));
    await rig.Cycle();
    rig.Full.Should().Be(2);
  }

  [Theory]
  [InlineData(1000)]
  [InlineData(10_000_000)]
  [InlineData(1_000_000_000)]
  public async Task Failure_cools_down_for_a_complete_real_interval(long frequency)
  {
    using var rig = new Rig(frequency);
    await rig.Cycle();
    rig.Time.Advance(TimeSpan.FromSeconds(2));
    rig.Coordinator.NoteCycleFailure(15);
    rig.Coordinator.ComputeWaitDelay().Should().Be(TimeSpan.FromSeconds(15));
    rig.Time.Advance(TimeSpan.FromSeconds(14));
    await rig.Cycle();
    rig.Full.Should().Be(1);
    rig.Time.Advance(TimeSpan.FromSeconds(1));
    await rig.Cycle();
    rig.Full.Should().Be(2);
  }

  private sealed class Clock(long frequency) : TimeProvider
  {
    private long _timestamp;
    public override long TimestampFrequency => frequency;
    public override long GetTimestamp() => _timestamp;
    public void Advance(TimeSpan elapsed) => _timestamp += (long)(elapsed.TotalSeconds * frequency);
  }

  private sealed class Rig : IDisposable
  {
    private const string Target = @"C:\Windows\System32\notepad.exe";
    private readonly SemaphoreSlim _wake = new(0);
    public readonly Clock Time;
    public readonly GuardianCycleCoordinator Coordinator;
    public int Full, Reads;
    public bool Admitted;

    public Rig(long frequency)
    {
      Time = new Clock(frequency);
      var config = GuardConfig.CreateDefault(Path.GetTempPath());
      config.ForegroundRestrictionEnabled = true;
      config.ForegroundProgramPaths = [Target];
      var state = new GuardianLoopIterationState { LastForegroundAdmitted = false };
      Coordinator = new GuardianCycleCoordinator(() => GuardConfigReadResult.FromConfig(config),
        () => { Reads++; return ForegroundObservation.Identified(Admitted ? Target : @"C:\outside.exe"); },
        state, _wake, (_, _) => { Full++; state.LastForegroundAdmitted = Admitted; return Task.CompletedTask; }, Time);
    }

    public Task Cycle() => Coordinator.RunCycleAsync(GuardianDirective(), 15, CancellationToken.None);
    private static PowerLifecycleDirective GuardianDirective() => new PowerLifecycleStateMachine().TakeDirective();
    public void Dispose() => _wake.Dispose();
  }
}
