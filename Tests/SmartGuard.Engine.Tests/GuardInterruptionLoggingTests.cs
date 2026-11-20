using System.Reflection;
using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

[Collection("LoggingFallbackTests")]
public sealed class GuardInterruptionLoggingTests : IDisposable
{
  private readonly string _root = Path.Combine(Path.GetTempPath(), "SmartGuardInterruption_" + Guid.NewGuid().ToString("N"));
  public GuardInterruptionLoggingTests() => Directory.CreateDirectory(_root);
  private string ConfigPath => SmartGuardPaths.ConfigFile(_root);
  private GuardianLoop CreateLoop() => new(_root, ConfigPath, SmartGuardPaths.StatusFile(_root),
    SmartGuardPaths.InitMarkerFile(_root), SmartGuardPaths.StartupLogFile(_root), false);
  private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
  private static void Drain(GuardianLoop loop) => typeof(GuardianLoop).GetMethod("DrainGuardObservations", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(loop, null);

  [Fact]
  public async Task First_invalid_config_uses_fallback_without_attempting_a_default_main_sink()
  {
    File.WriteAllText(ConfigPath, "{", new System.Text.UTF8Encoding(false));
    var loop = CreateLoop();
    var write = typeof(GuardianLoop).GetMethod("WriteLog", BindingFlags.NonPublic | BindingFlags.Instance)!;
    var accepted = (GuardianLogWriteResult)write.Invoke(loop, [null, LogLevel.Warn, "配置不可用", EventNameRegistry.GuardStateChanged, null, null, null])!;
    accepted.Should().Be(GuardianLogWriteResult.AcceptedByPipeline);
    var pipeline = Field<LogPipeline>(loop, "_logPipeline");
    (await pipeline.FlushAsync()).Should().BeTrue();
    pipeline.Diagnostics.PrimaryWrittenLines.Should().Be(0);
    pipeline.Diagnostics.FallbackWrittenLines.Should().Be(1);
    pipeline.Diagnostics.PrimaryFailedBatches.Should().Be(0);
    File.Exists(SmartGuardPaths.DefaultLogFile(_root)).Should().BeFalse();
    File.Exists(SmartGuardPaths.StartupLogFile(_root)).Should().BeTrue();
    (await pipeline.StopAsync()).Should().BeTrue();
  }

  [Fact]
  public async Task Invalid_config_preserves_only_last_valid_logging_level_and_destination()
  {
    var config = GuardConfig.CreateDefault(_root);
    config.LogMinimumLevel = "Critical";
    config.LogFile = Path.Combine(_root, "custom.log");
    var repo = new GuardConfigRepository(ConfigPath);
    repo.Save(config);
    var loop = CreateLoop();
    var write = typeof(GuardianLoop).GetMethod("WriteLog", BindingFlags.NonPublic | BindingFlags.Instance)!;
    write.Invoke(loop, [config, LogLevel.Info, "filtered", EventNameRegistry.ConfigReloaded, null, null, null]);
    File.WriteAllText(ConfigPath, "{", new System.Text.UTF8Encoding(false));
    ((GuardianLogWriteResult)write.Invoke(loop, [null, LogLevel.Warn, "filtered too", EventNameRegistry.GuardStateChanged, null, null, null])!)
      .Should().Be(GuardianLogWriteResult.SuppressedByLevel);
    write.Invoke(loop, [null, LogLevel.Critical, "controlled failure", EventNameRegistry.GuardStateChanged, null, null, null]);
    var pipeline = Field<LogPipeline>(loop, "_logPipeline");
    (await pipeline.FlushAsync()).Should().BeTrue();
    File.Exists(config.LogFile).Should().BeTrue();
    File.Exists(SmartGuardPaths.DefaultLogFile(_root)).Should().BeFalse();
    (await pipeline.StopAsync()).Should().BeTrue();
    repo.TryLoadDetailed().IsValid.Should().BeFalse("the logging snapshot never restores action authority");
  }

  [Fact]
  public async Task Rule_content_adoption_ignores_theme_detects_same_count_replacement_and_reports_recovery()
  {
    var repo = new GuardConfigRepository(ConfigPath);
    var config = GuardConfig.CreateDefault(_root);
    config.ForegroundRestrictionEnabled = true;
    config.ForegroundProgramPaths = [@"C:\One\app.exe"];
    repo.Save(config);
    var loop = CreateLoop();
    var pipeline = Field<LogPipeline>(loop, "_logPipeline");
    Drain(loop);
    (await pipeline.FlushAsync()).Should().BeTrue();
    config.ThemeIsDark = !config.ThemeIsDark;
    repo.Save(config);
    Drain(loop);
    config.ForegroundProgramPaths = [@"C:\Two\app.exe"];
    repo.Save(config);
    Drain(loop);
    File.WriteAllText(ConfigPath, "{", new System.Text.UTF8Encoding(false));
    Drain(loop);
    repo.Save(config);
    Drain(loop);
    (await pipeline.FlushAsync()).Should().BeTrue();
    var events = File.ReadLines(SmartGuardPaths.ResolveLogFile(config, _root))
      .Select(line => { LogJsonlFormatter.TryParseLine(line, out var item).Should().BeTrue(); return item!; })
      .Where(e => e.EventName == EventNameRegistry.ConfigReloaded).ToArray();
    events.Should().HaveCount(3, "initial adoption, same-count replacement, and recovery only");
    events[2].Message.Should().Contain("读取恢复");
    events.Should().OnlyContain(e => !e.Message.Contains(@"C:\", StringComparison.Ordinal));
    (await pipeline.StopAsync()).Should().BeTrue();
  }

  [Fact]
  public async Task Repeated_parse_failure_warns_once_without_authorizing_actions_and_keeps_controlled_reason()
  {
    var clock = new ManualTimeProvider();
    var tracker = new GuardObservationTracker();
    var events = new List<(LogLevel Level, string Message, string Name)>();
    GuardianLogWriteResult Write(GuardConfig? _, LogLevel level, string message, string name,
      IReadOnlyDictionary<string, JsonElement>? properties, DateTimeOffset? at)
    { events.Add((level, message, name)); return GuardianLogWriteResult.AcceptedByPipeline; }
    var read = new GuardConfigReadResult(null, false, "parse");
    var runner = new GuardianIterationRunner(new IdleTracker(clock), new BrightnessService(),
      new StatusPublisher(SmartGuardPaths.StatusFile(_root), clock), () => read, Write,
      _ => { }, () => false, new HeartbeatSchedule(clock), readIdleSeconds: () => 0,
      readBatteryInfo: _ => (90, true), readCurrentPlan: () => new(true, new PowerSchemeInfo(Guid.Empty, null), PowerPlanFailureKind.None, ""),
      applyPlan: (_, _) => throw new InvalidOperationException("Unavailable cannot authorize"),
      timeProvider: clock, readForeground: () => throw new InvalidOperationException("Unavailable cannot query identity"),
      readBrightness: () => -1, observationTracker: tracker);
    var state = new GuardianLoopIterationState { PlanCatalog = new Dictionary<Guid, string>() };
    for (var i = 0; i < 3; i++)
    {
      await runner.RunAsync(state, GuardianIterationContext.Running, CancellationToken.None);
      tracker.Drain(null, Write);
      clock.Advance(TimeSpan.FromSeconds(15));
    }
    var failed = events.Where(e => e.Name == EventNameRegistry.GuardStateChanged).ToArray();
    failed.Should().ContainSingle();
    failed[0].Level.Should().Be(LogLevel.Warn);
    failed[0].Message.Should().Contain("解析错误");
    StatusJsonReader.TryRead(SmartGuardPaths.StatusFile(_root))!.guardReason.Should().Be(GuardStatusCodes.Reasons.ConfigUnavailable);
  }

  [Theory]
  [InlineData("matched", "foreground-matched", "already-satisfied")]
  [InlineData("unknown", "foreground-identity-unknown", "not-attempted")]
  [InlineData("empty", "foreground-list-empty", "not-attempted")]
  [InlineData("invalid", "foreground-program-paths-invalid", "not-attempted")]
  [InlineData("miss", "waiting-for-listed-program", "not-attempted")]
  [InlineData("paused", "paused", "not-attempted")]
  [InlineData("manual", "manual-active", "already-satisfied")]
  [InlineData("expired-manual", "foreground-matched", "already-satisfied")]
  [InlineData("blocked", "blocked-by-lifecycle", "not-attempted")]
  [InlineData("failed", "foreground-matched", "failed")]
  [InlineData("probe", "probe-failed", "not-attempted")]
  public async Task Final_reason_and_action_are_explained_and_heartbeat_never_invents_success(string scenario, string reason, string action)
  {
    var config = GuardConfig.CreateDefault(_root);
    var clock = new ManualTimeProvider();
    config.ForegroundRestrictionEnabled = true;
    config.ForegroundProgramPaths = [@"C:\Apps\app.exe"];
    config.HeartbeatIntervalMin = 1;
    config.BrightnessRestoreMs = 0;
    if (scenario == "paused") config.Paused = true;
    if (scenario == "manual") config.ManualHighPerformanceUntil = clock.GetLocalNow().LocalDateTime.AddMinutes(1);
    if (scenario == "expired-manual") config.ManualHighPerformanceUntil = clock.GetLocalNow().LocalDateTime.AddMinutes(-1);
    if (scenario == "empty") config.ForegroundProgramPaths = [];
    if (scenario == "invalid") config.ForegroundProgramPaths = ["relative.exe"];
    var tracker = new GuardObservationTracker();
    var events = new List<LogEvent>();
    GuardianLogWriteResult Write(GuardConfig? _, LogLevel level, string message, string name,
      IReadOnlyDictionary<string, JsonElement>? properties, DateTimeOffset? at)
    { events.Add(EngineLogEvents.Create(level, name, message, properties: properties)); return GuardianLogWriteResult.AcceptedByPipeline; }
    var queries = 0;
    var applies = 0;
    var runner = new GuardianIterationRunner(new IdleTracker(clock), new BrightnessService(),
      new StatusPublisher(SmartGuardPaths.StatusFile(_root), clock), () => GuardConfigReadResult.FromConfig(config), Write,
      _ => { }, () => false, new HeartbeatSchedule(clock), readIdleSeconds: () => 0,
      readBatteryInfo: _ => (90, true), readCurrentPlan: () => new(scenario != "probe",
        scenario == "probe" ? null : new PowerSchemeInfo(scenario == "failed" ? config.BalancedPlanGuid : config.ActivePlanGuid, null),
        scenario == "probe" ? PowerPlanFailureKind.ApiFailed : PowerPlanFailureKind.None, "private diagnostic omitted"),
      applyPlan: (target, _) => { applies++; return Task.FromResult(new PowerPlanApplyResult(false, target, config.BalancedPlanGuid, PowerPlanFailureKind.VerificationMismatch, "private diagnostic omitted")); },
      timeProvider: clock, readForeground: () =>
      {
        queries++;
        return scenario == "unknown" ? ForegroundObservation.Unknown : ForegroundObservation.Identified(scenario == "miss" ? @"C:\Other\app.exe" : @"C:\Apps\app.exe");
      }, readBrightness: () => -1, observationTracker: tracker);
    var state = new GuardianLoopIterationState { PlanCatalog = new Dictionary<Guid, string>() };
    var context = scenario == "blocked" ? GuardianIterationContext.Running with { AllowPolicyMutation = false } : GuardianIterationContext.Running;
    await runner.RunAsync(state, context, CancellationToken.None);
    tracker.Drain(config, Write);
    var guard = events.Single(e => e.EventName == EventNameRegistry.GuardStateChanged);
    guard.Properties!["guardReason"].GetString().Should().Be(reason);
    guard.Properties["actionResult"].GetString().Should().Be(action);
    events.Should().OnlyContain(e => !e.Message.Contains("计划正常", StringComparison.Ordinal));
    if (scenario is "empty" or "invalid" or "probe") queries.Should().Be(0);
    applies.Should().Be(scenario == "failed" ? 1 : 0);
    var heartbeats = events.Where(e => e.EventName == EventNameRegistry.HeartbeatTick).ToArray();
    if (scenario is "failed" or "probe") heartbeats.Should().BeEmpty();
    else
    {
      heartbeats.Should().ContainSingle();
      if (action == "already-satisfied") heartbeats[0].Message.Should().Contain("符合设置");
      else heartbeats[0].Message.Should().NotContain("符合设置");
    }
  }

  [Fact]
  public async Task Light_config_failure_is_visible_without_new_system_probes_or_old_execution_facts()
  {
    var config = GuardConfig.CreateDefault(_root);
    config.ForegroundRestrictionEnabled = true;
    config.ForegroundProgramPaths = [@"C:\Apps\app.exe"];
    var read = new GuardConfigReadResult(config, true, RuleFingerprint: "rules-1");
    var clock = new ManualTimeProvider();
    var tracker = new GuardObservationTracker();
    using var wake = new SemaphoreSlim(0);
    var full = 0;
    var queries = 0;
    var coordinator = new GuardianCycleCoordinator(() => read,
      () => { queries++; return ForegroundObservation.Identified(@"C:\Apps\app.exe"); },
      new GuardianLoopIterationState { LastForegroundAdmitted = true }, wake,
      (_, _) => { full++; return Task.CompletedTask; }, clock, observationTracker: tracker);
    var directive = new PowerLifecycleStateMachine(clock).TakeDirective();
    await coordinator.RunCycleAsync(directive, 60, CancellationToken.None);
    read = new(null, false, "io");
    clock.Advance(TimeSpan.FromSeconds(1));
    coordinator.MarkForegroundSignal();
    await coordinator.RunCycleAsync(directive, 60, CancellationToken.None);
    var events = new List<LogEvent>();
    tracker.Drain(null, (_, level, message, name, properties, _) =>
    { events.Add(EngineLogEvents.Create(level, name, message, properties: properties)); return GuardianLogWriteResult.AcceptedByPipeline; });
    events.Should().ContainSingle().Which.Severity.Should().Be(LogSeverity.Warn);
    events[0].Properties.Should().NotContainKey("actualPlanGuid").And.NotContainKey("targetPlanGuid");
    events[0].Message.Should().Contain("读取文件失败");
    full.Should().Be(1);
    queries.Should().Be(0);
  }

  [Fact]
  public void Startup_initialization_does_not_overwrite_an_existing_unreadable_configuration()
  {
    Directory.CreateDirectory(ConfigPath);
    var loop = CreateLoop();
    Action ensure = () => typeof(GuardianLoop).GetMethod("EnsureConfigExists", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(loop, null);
    ensure.Should().NotThrow("unreadable is not the same as missing and must enter controlled observation");
    Directory.Exists(ConfigPath).Should().BeTrue();
  }

  [Fact]
  public void Failed_initialization_stays_unavailable_and_confirmed_missing_can_still_initialize()
  {
    var blockedRoot = Path.Combine(_root, "root-file");
    File.WriteAllText(blockedRoot, "marker", new System.Text.UTF8Encoding(false));
    var blockedConfig = SmartGuardPaths.ConfigFile(blockedRoot);
    var loop = new GuardianLoop(blockedRoot, blockedConfig, SmartGuardPaths.StatusFile(blockedRoot),
      SmartGuardPaths.InitMarkerFile(blockedRoot), SmartGuardPaths.StartupLogFile(blockedRoot), false);
    Action ensure = () => typeof(GuardianLoop).GetMethod("EnsureConfigExists", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(loop, null);
    ensure.Should().NotThrow("failed initialization must leave the cycle able to report unavailability");
    GuardConfigReadResult.TryFromRepository(new GuardConfigRepository(blockedConfig)).IsValid.Should().BeFalse();
    var normal = CreateLoop();
    typeof(GuardianLoop).GetMethod("EnsureConfigExists", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(normal, null);
    new GuardConfigRepository(ConfigPath).TryLoadDetailed().IsValid.Should().BeTrue("initial default creation remains supported for confirmed missing files");
  }

  public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
