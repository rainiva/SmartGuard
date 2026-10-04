using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;
using SmartGuard.Contracts;
using System.Text.Json;
using System.Reflection;

namespace SmartGuard.Engine.Tests;

public sealed class GuardRuleLoggingTests : IDisposable
{
  private readonly string _root = Path.Combine(Path.GetTempPath(), "SmartGuardRuleLogs_" + Guid.NewGuid().ToString("N"));
  public GuardRuleLoggingTests() => Directory.CreateDirectory(_root);

  [Fact]
  public async Task Listed_program_already_at_target_records_admission_without_applying_or_probing_again()
  {
    var config = GuardConfig.CreateDefault(_root);
    config.ForegroundRestrictionEnabled = true;
    config.ForegroundProgramPaths = [@"C:\Private\app.exe"];
    config.HeartbeatIntervalMin = 0;
    var events = new List<string>();
    var reads = 0;
    var applies = 0;
    var tracker = new GuardObservationTracker();
    var runner = new GuardianIterationRunner(
      new IdleTracker(), new BrightnessService(), new StatusPublisher(Path.Combine(_root, "status.json")),
      () => GuardConfigReadResult.FromConfig(config),
      (_, _, _, eventName, _, _) => { events.Add(eventName); return GuardianLogWriteResult.AcceptedByPipeline; },
      _ => { }, false, new HeartbeatSchedule(), readIdleSeconds: () => 0,
      readBatteryInfo: _ => (90, true),
      readCurrentPlan: () => { reads++; return new(true, new PowerSchemeInfo(config.ActivePlanGuid, null), PowerPlanFailureKind.None, ""); },
      applyPlan: (target, _) => { applies++; return Task.FromResult(new PowerPlanApplyResult(true, target, target, PowerPlanFailureKind.None, "")); },
      readForeground: () => ForegroundObservation.Identified(config.ForegroundProgramPaths[0]), readBrightness: () => -1,
      observationTracker: tracker);

    await runner.RunAsync(new GuardianLoopIterationState { PlanCatalog = new Dictionary<Guid, string>() },
      GuardianIterationContext.Running, CancellationToken.None);
    tracker.Drain(config, (_, _, _, eventName, _, _) => { events.Add(eventName); return GuardianLogWriteResult.AcceptedByPipeline; });

    events.Should().Contain("ForegroundAdmissionChanged");
    events.Should().Contain("GuardStateChanged");
    applies.Should().Be(0);
    reads.Should().Be(1, "logging must reuse the existing observation");
  }

  [Fact]
  public void Admission_exposes_original_rule_index_without_a_second_matching_algorithm()
  {
    var config = GuardConfig.CreateDefault(_root);
    config.ForegroundRestrictionEnabled = true;
    config.ForegroundProgramPaths = [@"C:\First\app.exe", @"C:\Second\app.exe"];
    var result = PolicyEngine.EvaluateAdmission(ForegroundObservation.Identified(@"c:\second\APP.EXE"), config);
    var field = typeof(ForegroundAdmission).GetProperty("MatchedRuleIndex");
    field.Should().NotBeNull();
    field!.GetValue(result).Should().Be(1);
  }

  [Fact]
  public void Stable_rejected_observation_retains_its_time_and_parts_acknowledge_independently()
  {
    var tracker = new GuardObservationTracker();
    var at = DateTimeOffset.UtcNow.AddMinutes(-1);
    var read = new GuardConfigReadResult(GuardConfig.CreateDefault(_root), true, RuleFingerprint: "rules-1");
    tracker.ObserveContext(read, "restricted");
    tracker.ObserveAdmission(new(true, GuardDecisionReason.ForegroundMatched, 0), @"C:\Private\app.exe", at);
    tracker.ObserveExecution("automatic", "foreground-matched", Guid.Empty, Guid.Empty, "already-satisfied", at);
    var events = new List<Observed>();
    tracker.Drain(read.Config, (_, level, message, name, properties, timestamp) =>
    {
      events.Add(new(name, level, message, properties!, timestamp!.Value));
      return name == "GuardStateChanged" ? GuardianLogWriteResult.Rejected : GuardianLogWriteResult.AcceptedByPipeline;
    });
    tracker.ObserveAdmission(new(true, GuardDecisionReason.ForegroundMatched, 0), "APP.EXE", at.AddSeconds(20));
    tracker.ObserveExecution("automatic", "foreground-matched", Guid.Empty, Guid.Empty, "already-satisfied", at.AddSeconds(20));
    tracker.Drain(read.Config, Capture(events));
    events.Select(e => e.Name).Should().Equal("ForegroundAdmissionChanged", "GuardStateChanged", "GuardStateChanged");
    events[^1].At.Should().Be(at);
    events[^1].Properties["observedAtUtc"].GetDateTimeOffset().Should().Be(at);
  }

  [Theory]
  [InlineData("restricted", "rules-2")]
  [InlineData("paused", "rules-1")]
  [InlineData("manual-active", "rules-1")]
  [InlineData("unrestricted", "rules-1")]
  [InlineData("config-unavailable", null)]
  public void Changed_generation_discards_old_rejected_execution(string gate, string? fingerprint)
  {
    var tracker = new GuardObservationTracker();
    var config = GuardConfig.CreateDefault(_root);
    tracker.ObserveContext(new(config, true, RuleFingerprint: "rules-1"), "restricted");
    tracker.ObserveExecution("automatic", "foreground-matched", Guid.Empty, Guid.Empty, "verified", DateTimeOffset.UtcNow);
    tracker.Drain(config, (_, _, _, _, _, _) => GuardianLogWriteResult.Rejected);
    tracker.ObserveContext(new(config, true, RuleFingerprint: fingerprint), gate);
    var events = new List<Observed>();
    tracker.Drain(config, Capture(events));
    events.Should().BeEmpty("an old automatic success cannot cross generations");
  }

  [Fact]
  public void Repeated_held_state_does_not_retimestamp_rejected_execution()
  {
    var tracker = new GuardObservationTracker();
    var config = GuardConfig.CreateDefault(_root);
    var at = DateTimeOffset.UtcNow;
    var admission = new ForegroundAdmission(false, GuardDecisionReason.WaitingForListedProgram);
    tracker.ObserveAdmission(admission, null, at);
    tracker.ObserveExecution("none", "waiting-for-listed-program", null, Guid.Empty, "not-attempted", at);
    tracker.Drain(config, (_, _, _, _, _, _) => GuardianLogWriteResult.Rejected);
    tracker.ObserveAdmission(admission, null, at.AddSeconds(5));
    tracker.ObserveExecution("none", "waiting-for-listed-program", null, Guid.Empty, "not-attempted", at.AddSeconds(5));
    var events = new List<Observed>();
    tracker.Drain(config, Capture(events));
    events.Last().At.Should().Be(at);
  }

  [Fact]
  public void Stable_state_is_quiet_and_user_filter_does_not_retry_until_threshold_changes()
  {
    var config = GuardConfig.CreateDefault(_root);
    config.LogMinimumLevel = "Warn";
    var read = new GuardConfigReadResult(config, true, RuleFingerprint: "rules-1");
    var tracker = new GuardObservationTracker();
    var attempts = 0;
    for (var i = 0; i < 1000; i++)
    {
      tracker.ObserveContext(read, "restricted");
      tracker.ObserveAdmission(new(true, GuardDecisionReason.ForegroundMatched, 0), "app.exe", DateTimeOffset.UtcNow);
      tracker.Drain(config, (_, _, _, _, _, _) => { attempts++; return GuardianLogWriteResult.SuppressedByLevel; });
    }
    attempts.Should().Be(1);
    config.LogMinimumLevel = "Info";
    tracker.ObserveContext(read, "restricted");
    tracker.Drain(config, (_, _, _, _, _, _) => { attempts++; return GuardianLogWriteResult.AcceptedByPipeline; });
    tracker.Drain(config, (_, _, _, _, _, _) => throw new InvalidOperationException("Stable observation must be quiet"));
    attempts.Should().Be(2);
  }

  [Fact]
  public async Task Light_A_to_B_and_exit_emit_admission_without_an_extra_full_iteration()
  {
    var config = GuardConfig.CreateDefault(_root);
    config.ForegroundRestrictionEnabled = true;
    config.ForegroundProgramPaths = [@"C:\A\app.exe", @"C:\B\app.exe"];
    var clock = new ManualTimeProvider();
    var tracker = new GuardObservationTracker();
    var read = new GuardConfigReadResult(config, true, RuleFingerprint: "rules-1");
    var state = new GuardianLoopIterationState { LastForegroundAdmitted = true };
    var foreground = ForegroundObservation.Identified(config.ForegroundProgramPaths[1]);
    using var wake = new SemaphoreSlim(0);
    var full = 0;
    var queries = 0;
    var coordinator = new GuardianCycleCoordinator(() => read, () => { queries++; return foreground; }, state, wake,
      (_, _) => { full++; return Task.CompletedTask; }, clock, observationTracker: tracker);
    var directive = new PowerLifecycleStateMachine(clock).TakeDirective();
    await coordinator.RunCycleAsync(directive, 60, CancellationToken.None);
    tracker.ObserveContext(read, "restricted");
    tracker.ObserveAdmission(new(true, GuardDecisionReason.ForegroundMatched, 0), config.ForegroundProgramPaths[0], clock.GetUtcNow());
    tracker.Drain(config, (_, _, _, _, _, _) => GuardianLogWriteResult.AcceptedByPipeline);
    var events = new List<Observed>();
    clock.Advance(TimeSpan.FromSeconds(1));
    coordinator.MarkForegroundSignal();
    await coordinator.RunCycleAsync(directive, 60, CancellationToken.None);
    tracker.Drain(config, Capture(events));
    events.Single().Properties["matchedRuleIndex"].GetInt32().Should().Be(1);
    events.Single().Properties.Should().NotContainKey("targetPlanGuid");
    events.Single().Message.Should().NotContain(@"C:\");
    foreground = ForegroundObservation.Identified(@"C:\Other\app.exe");
    clock.Advance(TimeSpan.FromSeconds(1));
    coordinator.MarkForegroundSignal();
    await coordinator.RunCycleAsync(directive, 60, CancellationToken.None);
    tracker.Drain(config, Capture(events));
    events.Last().Message.Should().Contain("退出命中");
    state.LastForegroundAdmitted.Should().BeFalse();
    full.Should().Be(1);
    queries.Should().Be(2);
  }

  [Fact]
  public async Task Loop_keeps_original_observation_timestamp_in_canonical_jsonl()
  {
    var configPath = Path.Combine(_root, "config.json");
    var config = GuardConfig.CreateDefault(_root);
    new GuardConfigRepository(configPath).Save(config);
    var loop = new GuardianLoop(_root, configPath, Path.Combine(_root, "status.json"),
      Path.Combine(_root, "marker"), Path.Combine(_root, "startup.log"), false);
    var at = DateTimeOffset.UtcNow.AddMinutes(-2);
    var tracker = (GuardObservationTracker)typeof(GuardianLoop).GetField("_guardObservations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(loop)!;
    var read = GuardConfigReadResult.TryFromRepository(new GuardConfigRepository(configPath));
    tracker.ObserveContext(read, GuardObservationGate.For(read, DateTimeOffset.Now));
    tracker.ObserveAdmission(new(true, GuardDecisionReason.ForegroundMatched, 0), "app.exe", at);
    typeof(GuardianLoop).GetMethod("DrainGuardObservations", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(loop, null);
    var pipeline = (LogPipeline)typeof(GuardianLoop).GetField("_logPipeline", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(loop)!;
    (await pipeline.FlushAsync()).Should().BeTrue();
    var line = File.ReadLines(SmartGuardPaths.ResolveLogFile(config, _root)).Single();
    using var parsed = JsonDocument.Parse(line);
    parsed.RootElement.GetProperty("timestamp").GetDateTimeOffset().Should().Be(at);
    parsed.RootElement.GetProperty("properties").GetProperty("observedAtUtc").GetDateTimeOffset().Should().Be(at);
    (await pipeline.StopAsync()).Should().BeTrue();
  }

  private sealed record Observed(string Name, LogLevel Level, string Message, IReadOnlyDictionary<string, JsonElement> Properties, DateTimeOffset At);
  private static Func<GuardConfig?, LogLevel, string, string, IReadOnlyDictionary<string, JsonElement>?, DateTimeOffset?, GuardianLogWriteResult> Capture(List<Observed> events)
    => (_, level, message, name, properties, at) => { events.Add(new(name, level, message, properties!, at!.Value)); return GuardianLogWriteResult.AcceptedByPipeline; };

  public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
