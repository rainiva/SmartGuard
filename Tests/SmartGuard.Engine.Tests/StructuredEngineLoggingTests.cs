using SmartGuard.Configuration;
using SmartGuard.Engine.Infrastructure;

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
      "on AC");

    logEvent.Channel.Should().Be(LogChannelRegistry.Power);
  }
}
