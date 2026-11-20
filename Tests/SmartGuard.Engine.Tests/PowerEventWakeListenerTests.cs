using FluentAssertions;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class PowerEventWakeListenerTests
{
  private sealed class Subscription(Action dispose) : IDisposable
  {
    public void Dispose() => dispose();
  }

  [Fact]
  public void Native_suspend_resume_is_typed_deduplicated_and_stops_after_dispose()
  {
    Action<uint>? native = null;
    Microsoft.Win32.PowerModeChangedEventHandler? system = null;
    var signals = new List<PowerLifecycleSignal>();
    var time = new ManualTimeProvider();
    var unregistered = 0;
    PowerEventWakeListener.SubscribeOverrideForTests = handler => system = handler;
    PowerEventWakeListener.NativeRegisterOverrideForTests = callback =>
    {
      native = callback;
      return new Subscription(() => unregistered++);
    };
    try
    {
      var listener = PowerEventWakeListener.Create(signals.Add, time);
      native.Should().NotBeNull("S0 requires explicit native registration");
      native!(4); native(18); native(7);
      system!(null, new(Microsoft.Win32.PowerModes.Suspend));
      system(null, new(Microsoft.Win32.PowerModes.Resume));
      system(null, new(Microsoft.Win32.PowerModes.StatusChange));
      signals.Select(s => s.Kind).Should().Equal(PowerLifecycleKind.Suspending, PowerLifecycleKind.Resumed, PowerLifecycleKind.StatusChanged);
      signals.Should().OnlyContain(s => s.ObservedAtUtc == time.GetUtcNow());
      native(4); native(7); native(18);
      signals.Count.Should().Be(5);
      listener.Dispose(); listener.Dispose(); native(4);
      signals.Count.Should().Be(5);
      unregistered.Should().Be(1);
    }
    finally
    {
      PowerEventWakeListener.SubscribeOverrideForTests = null;
      PowerEventWakeListener.NativeRegisterOverrideForTests = null;
    }
  }

  [Fact]
  public void Native_callback_failure_is_contained_and_marks_listener_unhealthy()
  {
    Action<uint>? native = null;
    PowerEventWakeListener.SubscribeOverrideForTests = _ => { };
    PowerEventWakeListener.NativeRegisterOverrideForTests = callback =>
    {
      native = callback;
      callback(4); // Registration may deliver a callback before returning.
      return new Subscription(() => { });
    };
    try
    {
      using var listener = PowerEventWakeListener.Create(_ => throw new InvalidOperationException("consumer failed"));
      native.Should().NotBeNull();
      Action callback = () => native!(4);
      callback.Should().NotThrow();
      listener.IsEnabled.Should().BeFalse();
    }
    finally
    {
      PowerEventWakeListener.SubscribeOverrideForTests = null;
      PowerEventWakeListener.NativeRegisterOverrideForTests = null;
    }
  }

  [Fact]
  public void Native_registration_failure_disables_listener_and_removes_system_subscription()
  {
    var removed = 0;
    PowerEventWakeListener.SubscribeOverrideForTests = _ => { };
    PowerEventWakeListener.UnsubscribeOverrideForTests = _ => removed++;
    PowerEventWakeListener.NativeRegisterOverrideForTests = _ => throw new System.ComponentModel.Win32Exception(5);
    try
    {
      using var listener = PowerEventWakeListener.Create(_ => { });
      listener.IsEnabled.Should().BeFalse();
      removed.Should().Be(1);
    }
    finally
    {
      PowerEventWakeListener.SubscribeOverrideForTests = null;
      PowerEventWakeListener.UnsubscribeOverrideForTests = null;
      PowerEventWakeListener.NativeRegisterOverrideForTests = null;
    }
  }

  [Fact]
  public void Power_mode_signal_is_typed_and_does_not_read_battery_state()
  {
    PowerLifecycleSignal? observed = null;
    PowerEventWakeListener.SubscribeOverrideForTests = _ => { };

    try
    {
      using var listener = PowerEventWakeListener.Create(
        (PowerLifecycleSignal signal) => observed = signal);

      observed.Should().BeNull();
      listener.IsEnabled.Should().BeTrue();
    }
    finally
    {
      PowerEventWakeListener.SubscribeOverrideForTests = null;
    }
  }

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
