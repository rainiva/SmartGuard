using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

// Shares the collection of the other round tests: PowerSchemeTestScope swaps a process-wide executor.
[Collection("LoggingFallbackTests")]
public sealed class GuardLivenessRunnerTests
{
  private static readonly TimeSpan Round = TimeSpan.FromSeconds(15);

  private sealed class Harness : IDisposable
  {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SmartGuardLiveness_" + Guid.NewGuid().ToString("N"));
    internal readonly ManualTimeProvider Clock = new();
    internal readonly List<(string Name, IReadOnlyDictionary<string, JsonElement>? Properties)> Events = [];
    internal readonly GuardianLoopIterationState State = new() { PlanCatalog = new Dictionary<Guid, string>() };
    internal readonly GuardConfig Config;
    internal readonly GuardianIterationRunner Runner;
    internal bool ReadSucceeds;
    internal string StatusPath => Path.Combine(_root, "status.json");

    internal Harness(bool paused = false)
    {
      Directory.CreateDirectory(_root);
      Config = GuardConfig.CreateDefault(_root);
      Config.Paused = paused;
      // An empty program list keeps every round a non-switching decision: liveness never depends on it.
      Config.ForegroundRestrictionEnabled = true;
      Runner = new GuardianIterationRunner(new IdleTracker(), new BrightnessService(), new StatusPublisher(StatusPath, Clock),
        () => GuardConfigReadResult.FromConfig(Config),
        (_, _, _, eventName, properties, _) => { Events.Add((eventName, properties)); return GuardianLogWriteResult.AcceptedByPipeline; },
        _ => { }, () => true, new HeartbeatSchedule(), readIdleSeconds: () => 0,
        readBatteryInfo: _ => (90, true),
        readCurrentPlan: () => ReadSucceeds
          ? new(true, new PowerSchemeInfo(Config.ActivePlanGuid, null), PowerPlanFailureKind.None, "")
          : new(false, null, PowerPlanFailureKind.CallPending, ""),
        readBrightness: () => 50, timeProvider: Clock,
        applyPlan: (_, _) => throw new InvalidOperationException("Fixture forbids physical plan writes"));
    }

    internal async Task RoundsAsync(TimeSpan span, GuardianIterationContext? context = null)
    {
      await Runner.RunAsync(State, context ?? GuardianIterationContext.Running, CancellationToken.None);
      for (var elapsed = TimeSpan.Zero; elapsed < span; elapsed += Round)
      {
        Clock.Advance(Round);
        await Runner.RunAsync(State, context ?? GuardianIterationContext.Running, CancellationToken.None);
      }
    }

    internal StatusPayload Status()
      => JsonSerializer.Deserialize<StatusPayload>(File.ReadAllText(StatusPath))!;

    internal int Count(string name) => Events.Count(e => e.Name == name);

    public void Dispose() => Directory.Delete(_root, true);
  }

  private static Task InFakeNative(Func<Task> run) => PowerSchemeTestScope.WithNative(new FakePowrProfNative(), run);

  [Fact]
  public Task Ten_minutes_of_failed_reads_publish_one_stall_one_warning_and_one_notification() => InFakeNative(async () =>
  {
    using var h = new Harness();
    await h.RoundsAsync(GuardLivenessTracker.Threshold - Round);
    h.Status().guardHealth!.Code.Should().Be(GuardHealthCodes.Healthy);
    h.Count(EventNameRegistry.GuardStalled).Should().Be(0);

    await h.RoundsAsync(TimeSpan.FromMinutes(2));
    var status = h.Status();
    status.guardHealth!.Code.Should().Be(GuardHealthCodes.Stalled);
    status.guardHealth.LastFailureKind.Should().Be(GuardHealthCodes.FailureKinds.CallPending);
    status.guardReason.Should().Be(GuardStatusCodes.Reasons.ProbeFailed);
    status.notificationEvent!.kind.Should().Be(NotificationKinds.GuardStalled);
    h.Count(EventNameRegistry.GuardStalled).Should().Be(1, "one stall is logged once");
    h.Events.Single(e => e.Name == EventNameRegistry.GuardStalled).Properties!["lastFailureKind"].GetString()
      .Should().Be(GuardHealthCodes.FailureKinds.CallPending);
  });

  [Fact]
  public Task A_successful_read_ends_the_stall_once() => InFakeNative(async () =>
  {
    using var h = new Harness();
    await h.RoundsAsync(GuardLivenessTracker.Threshold + Round);
    h.ReadSucceeds = true;
    await h.RoundsAsync(TimeSpan.FromMinutes(1));
    h.Status().guardHealth!.Code.Should().Be(GuardHealthCodes.Healthy);
    h.Count(EventNameRegistry.GuardStallRecovered).Should().Be(1);
    h.Count(EventNameRegistry.GuardStalled).Should().Be(1);
  });

  [Fact]
  public Task Paused_guarding_is_not_applicable_and_never_stalls() => InFakeNative(async () =>
  {
    using var h = new Harness(paused: true);
    await h.RoundsAsync(TimeSpan.FromMinutes(30));
    h.Status().guardHealth!.Code.Should().Be(GuardHealthCodes.NotApplicable);
    h.Count(EventNameRegistry.GuardStalled).Should().Be(0);
  });

  [Fact]
  public Task Lifecycle_blocked_rounds_are_not_applicable() => InFakeNative(async () =>
  {
    using var h = new Harness();
    await h.RoundsAsync(TimeSpan.FromMinutes(30),
      new GuardianIterationContext(false, false, false, false, PowerLifecyclePhase.ResumeStabilizing));
    h.Status().guardHealth!.Code.Should().Be(GuardHealthCodes.NotApplicable);
    h.Count(EventNameRegistry.GuardStalled).Should().Be(0);
  });

  [Theory]
  [InlineData("GuardStalled")]
  [InlineData("GuardStallRecovered")]
  public void Stall_events_are_registered_on_the_recovery_channel(string eventName)
  {
    EventNameRegistry.IsRegistered(eventName).Should().BeTrue();
    LogEventChannels.ForEventName(eventName).Should().Be(LogChannelRegistry.Recovery);
  }
}
