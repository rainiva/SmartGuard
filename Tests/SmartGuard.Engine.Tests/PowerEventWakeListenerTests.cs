using FluentAssertions;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class PowerEventWakeListenerTests
{
  [Fact]
  public void Create_when_subscribe_throws_PlatformNotSupported_returns_disabled_listener()
  {
    PowerEventWakeListener.SubscribeOverrideForTests = _ =>
      throw new PlatformNotSupportedException("SystemEvents is not supported on this platform.");

    try
    {
      using var listener = PowerEventWakeListener.Create(_ => { });

      listener.IsEnabled.Should().BeFalse();
    }
    finally
    {
      PowerEventWakeListener.SubscribeOverrideForTests = null;
    }
  }

  [Fact]
  public void Create_when_subscribe_succeeds_returns_enabled_listener()
  {
    PowerEventWakeListener.SubscribeOverrideForTests = _ => { /* no-op subscribe */ };

    try
    {
      using var listener = PowerEventWakeListener.Create(_ => { });

      listener.IsEnabled.Should().BeTrue();
    }
    finally
    {
      PowerEventWakeListener.SubscribeOverrideForTests = null;
    }
  }
}
