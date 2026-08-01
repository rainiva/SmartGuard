using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class EventNameRegistryTests
{
  [Fact]
  public void BuiltIn_events_are_registered()
  {
    EventNameRegistry.IsRegistered(EventNameRegistry.AppStarted).Should().BeTrue();
    EventNameRegistry.IsRegistered(EventNameRegistry.AppStopping).Should().BeTrue();
    EventNameRegistry.IsRegistered(EventNameRegistry.PowerPlanSwitched).Should().BeTrue();
    EventNameRegistry.IsRegistered(EventNameRegistry.HeartbeatTick).Should().BeTrue();
    EventNameRegistry.IsRegistered(EventNameRegistry.ExternalPlanChange).Should().BeTrue();
    EventNameRegistry.IsRegistered(EventNameRegistry.BatteryStatusPolled).Should().BeTrue();
    EventNameRegistry.IsRegistered(EventNameRegistry.ConfigReloaded).Should().BeTrue();
    EventNameRegistry.IsRegistered(EventNameRegistry.IterationError).Should().BeTrue();
  }

  [Fact]
  public void Unregistered_name_returns_false()
  {
    EventNameRegistry.IsRegistered("NonExistentEvent").Should().BeFalse();
  }

  [Fact]
  public void Register_adds_custom_event()
  {
    EventNameRegistry.Register("CustomTestEvent");
    EventNameRegistry.IsRegistered("CustomTestEvent").Should().BeTrue();
  }

  [Fact]
  public void AllRegistered_contains_builtIn_events()
  {
    var all = EventNameRegistry.AllRegistered;
    all.Should().Contain(EventNameRegistry.AppStarted);
    all.Should().Contain(EventNameRegistry.AppStopping);
  }

  [Fact]
  public void Register_duplicate_does_not_throw()
  {
    EventNameRegistry.Register("DuplicateEvent");
    EventNameRegistry.Register("DuplicateEvent"); // 幂等
    EventNameRegistry.IsRegistered("DuplicateEvent").Should().BeTrue();
  }

  [Fact]
  public void Register_rejects_null_or_empty()
  {
    var act1 = () => EventNameRegistry.Register(null!);
    var act2 = () => EventNameRegistry.Register("");
    act1.Should().Throw<ArgumentException>();
    act2.Should().Throw<ArgumentException>();
  }
}
