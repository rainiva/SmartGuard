using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

public sealed class ExternalPlanObservationLoggingTests : IDisposable
{
  private readonly string _root = Path.Combine(Path.GetTempPath(), "SmartGuardExternalObservation_" + Guid.NewGuid().ToString("N"));

  [Fact]
  public async Task Paused_external_change_does_not_promise_correction_in_log()
  {
    var (_, events, _, _) = await RunPausedObservationAsync();
    events.Where(e => e.EventName == EventNameRegistry.ExternalPlanChange)
      .Should().OnlyContain(e => !e.Message.Contains("纠偏", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Paused_external_notification_does_not_promise_correction()
  {
    var (_, _, notifications, _) = await RunPausedObservationAsync();
    notifications.Should().NotBeEmpty().And.OnlyContain(n => !n.body!.Contains("纠偏", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Repeated_external_observation_logs_once_without_changing_execution_baseline()
  {
    var (config, events, _, state) = await RunPausedObservationAsync();
    events.Count(e => e.EventName == EventNameRegistry.ExternalPlanChange).Should().Be(1);
    state.LastKnownGuid.Should().Be(config.BalancedPlanGuid, "observation deduplication must not change execution authority");
  }

  [Theory]
  [InlineData("changed", 3)]
  [InlineData("returned", 2)]
  [InlineData("rejected", 3)]
  [InlineData("filtered", 1)]
  public async Task Observation_acknowledgement_keeps_new_changes_and_failed_admission_visible(string scenario, int expectedAttempts)
  {
    var (_, events, _, _) = await RunPausedObservationAsync(scenario);
    events.Count(e => e.EventName == EventNameRegistry.ExternalPlanChange).Should().Be(expectedAttempts);
  }

  private async Task<(GuardConfig, List<LogEvent>, List<NotificationEvent>, GuardianLoopIterationState)> RunPausedObservationAsync(string scenario = "stable")
  {
    Directory.CreateDirectory(_root);
    var config = GuardConfig.CreateDefault(_root);
    config.Paused = true;
    var events = new List<LogEvent>();
    var notifications = new List<NotificationEvent>();
    var externalAttempts = 0;
    var iteration = 0;
    var state = new GuardianLoopIterationState { PlanCatalog = new Dictionary<Guid, string>(), LastKnownGuid = config.BalancedPlanGuid };
    GuardianLogWriteResult Write(GuardConfig? _, LogLevel level, string message, string name,
      IReadOnlyDictionary<string, JsonElement>? properties, DateTimeOffset? at)
    {
      events.Add(EngineLogEvents.Create(level, name, message, properties: properties));
      if (name == EventNameRegistry.ExternalPlanChange)
      {
        externalAttempts++;
        if (scenario == "rejected" && externalAttempts < 3) return GuardianLogWriteResult.Rejected;
        if (scenario == "filtered") return GuardianLogWriteResult.SuppressedByLevel;
      }
      return GuardianLogWriteResult.AcceptedByPipeline;
    }
    var runner = new GuardianIterationRunner(new IdleTracker(), new BrightnessService(),
      new StatusPublisher(SmartGuardPaths.StatusFile(_root)), () => GuardConfigReadResult.FromConfig(config), Write,
      _ => { }, false, new HeartbeatSchedule(), readIdleSeconds: () => 0,
      readBatteryInfo: _ => (90, true), readCurrentPlan: () => new(true,
        new PowerSchemeInfo(iteration == 1 && scenario == "changed" ? config.PowerSaverPlanGuid :
          iteration == 1 && scenario == "returned" ? config.BalancedPlanGuid : config.ActivePlanGuid, "Observed"), PowerPlanFailureKind.None, ""),
      applyPlan: (_, _) => throw new InvalidOperationException("Paused must never apply a plan"), readBrightness: () => -1);
    for (var i = 0; i < 3; i++)
    {
      iteration = i;
      await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
      var notification = StatusJsonReader.TryRead(SmartGuardPaths.StatusFile(_root))!.notificationEvent;
      if (notification is not null) notifications.Add(notification);
    }
    return (config, events, notifications, state);
  }

  public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
