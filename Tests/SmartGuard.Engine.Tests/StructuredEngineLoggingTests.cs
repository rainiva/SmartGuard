using System.Reflection;
using System.Text;
using SmartGuard.Configuration;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

public class StructuredEngineLoggingTests
{
  [Fact]
  public void Engine_lifecycle_event_carries_component_channel_and_event_name()
  {
    var logEvent = EngineLogEvents.Create(
      LogLevel.Info,
      EventNameRegistry.AppStarted,
      "engine started");

    logEvent.Component.Should().Be(LogComponentRegistry.Engine);
    logEvent.Channel.Should().Be(LogChannelRegistry.Lifecycle);
    logEvent.EventName.Should().Be(EventNameRegistry.AppStarted);
  }

  [Fact]
  public void Recovery_event_uses_recovery_channel()
  {
    var logEvent = EngineLogEvents.Create(
      LogLevel.Error,
      EventNameRegistry.RecoveryDegraded,
      "consumer failed");

    logEvent.Component.Should().Be(LogComponentRegistry.Engine);
    logEvent.Channel.Should().Be(LogChannelRegistry.Recovery);
    logEvent.EventName.Should().Be(EventNameRegistry.RecoveryDegraded);
  }

  [Fact]
  public void Power_event_uses_power_channel()
  {
    var logEvent = EngineLogEvents.Create(
      LogLevel.Info,
      EventNameRegistry.PowerPlanSwitched,
      "on AC",
      outcome: LogEventOutcome.Handled);

    logEvent.Channel.Should().Be(LogChannelRegistry.Power);
    logEvent.Properties[LogEventOutcome.PropertyName].GetString()
      .Should().Be(LogEventOutcome.Handled);
  }

  [Fact]
  public async Task GuardianLoop_lifecycle_transition_preserves_observed_time_in_structured_event()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-lifecycle-log-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var fallbackPath = Path.Combine(root, "SmartGuard.startup.log");

    try
    {
      var loop = new GuardianLoop(
        root,
        Path.Combine(root, "SmartGuard.config.json"),
        Path.Combine(root, "SmartGuard.status.json"),
        Path.Combine(root, ".SmartGuard.initialized"),
        fallbackPath,
        powerCfgBrightnessSupported: false);
      var writeTransitions = typeof(GuardianLoop).GetMethod(
        "WriteLifecycleTransitions",
        BindingFlags.NonPublic | BindingFlags.Instance)!;
      var pipeline = (LogPipeline)typeof(GuardianLoop).GetField(
        "_logPipeline",
        BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(loop)!;
      var observedAt = new DateTimeOffset(2026, 8, 22, 14, 0, 0, TimeSpan.Zero);
      var config = GuardConfig.CreateDefault(root);
      new GuardConfigRepository(SmartGuardPaths.ConfigFile(root)).Save(config);

      pipeline.Start();
      writeTransitions.Invoke(loop, [
        config,
        new[] { new PowerLifecycleSignal(PowerLifecycleKind.Resumed, observedAt) }
      ]);
      (await pipeline.StopAsync()).Should().BeTrue();

      var logPath = SmartGuardPaths.ResolveLogFile(config, root);
      var line = File.ReadAllLines(logPath, Encoding.UTF8).Should().ContainSingle().Subject;
      LogJsonlFormatter.TryParseLine(line, out var logEvent).Should().BeTrue();
      logEvent.Should().NotBeNull();
      logEvent!.EventName.Should().Be(EventNameRegistry.PowerResumeObserved);
      logEvent.Channel.Should().Be(LogChannelRegistry.Lifecycle);
      logEvent.Properties["lifecycleObservedAtUtc"].GetDateTimeOffset().Should().Be(observedAt);
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  [Fact]
  public async Task GuardianLoop_power_transition_writes_handled_structured_event()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-power-log-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var configPath = Path.Combine(root, "SmartGuard.config.json");
    new GuardConfigRepository(configPath).Save(GuardConfig.CreateDefault(root));
    var fallbackPath = Path.Combine(root, "SmartGuard.startup.log");

    try
    {
      var loop = new GuardianLoop(
        root,
        configPath,
        Path.Combine(root, "SmartGuard.status.json"),
        Path.Combine(root, ".SmartGuard.initialized"),
        fallbackPath,
        powerCfgBrightnessSupported: false);
      var observe = typeof(GuardianLoop).GetMethod(
        "ObservePowerSource",
        BindingFlags.NonPublic | BindingFlags.Instance)!;
      var pipeline = (LogPipeline)typeof(GuardianLoop).GetField(
        "_logPipeline",
        BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(loop)!;

      pipeline.Start();
      observe.Invoke(loop, [false]);
      observe.Invoke(loop, [true]);
      (await pipeline.StopAsync()).Should().BeTrue();

      var config = new GuardConfigRepository(configPath).LoadOrDefault(root);
      var logPath = SmartGuardPaths.ResolveLogFile(config, root);
      var line = File.ReadAllLines(logPath, Encoding.UTF8).Should().ContainSingle().Subject;
      LogJsonlFormatter.TryParseLine(line, out var logEvent).Should().BeTrue();
      logEvent.Should().NotBeNull();
      logEvent!.EventName.Should().Be(EventNameRegistry.PowerSourceChanged);
      logEvent.Channel.Should().Be(LogChannelRegistry.Power);
      logEvent.Message.Should().Contain("插电");
      logEvent.Properties[LogEventOutcome.PropertyName].GetString()
        .Should().Be(LogEventOutcome.Handled);
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }
}
