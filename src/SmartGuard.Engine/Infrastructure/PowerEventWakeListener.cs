using Microsoft.Win32;

namespace SmartGuard.Engine.Infrastructure;

internal sealed class PowerEventWakeListener : IDisposable
{
  private readonly Action<PowerLifecycleSignal> _onPowerChanged;
  private readonly TimeProvider _timeProvider;
  private bool _subscribed;
  private bool _usedTestOverride;

  internal static Action<PowerModeChangedEventHandler>? SubscribeOverrideForTests;
  internal static Action<PowerModeChangedEventHandler>? UnsubscribeOverrideForTests;

  public bool IsEnabled { get; private set; }

  private PowerEventWakeListener(
    Action<PowerLifecycleSignal> onPowerChanged,
    TimeProvider timeProvider,
    bool subscribe)
  {
    _onPowerChanged = onPowerChanged;
    _timeProvider = timeProvider;
    if (!subscribe)
      return;

    try
    {
      Subscribe();
      IsEnabled = true;
    }
    catch (PlatformNotSupportedException)
    {
      IsEnabled = false;
    }
  }

  internal PowerEventWakeListener(
    Action<PowerLifecycleSignal> onPowerChanged,
    TimeProvider? timeProvider = null)
    : this(onPowerChanged, timeProvider ?? TimeProvider.System, subscribe: true)
  {
  }

  internal static PowerEventWakeListener Create(
    Action<PowerLifecycleSignal> onPowerChanged,
    TimeProvider? timeProvider = null)
    => new(onPowerChanged, timeProvider ?? TimeProvider.System, subscribe: true);

  private void Subscribe()
  {
    if (SubscribeOverrideForTests is not null)
    {
      SubscribeOverrideForTests(OnPowerModeChanged);
      _usedTestOverride = true;
    }
    else
    {
      SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    _subscribed = true;
  }

  private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
  {
    var kind = e.Mode switch
    {
      PowerModes.Suspend => PowerLifecycleKind.Suspending,
      PowerModes.Resume => PowerLifecycleKind.Resumed,
      _ => PowerLifecycleKind.StatusChanged,
    };
    _onPowerChanged(new PowerLifecycleSignal(kind, _timeProvider.GetUtcNow()));
  }

  public void Dispose()
  {
    if (!_subscribed)
      return;

    if (_usedTestOverride)
      UnsubscribeOverrideForTests?.Invoke(OnPowerModeChanged);
    else
      SystemEvents.PowerModeChanged -= OnPowerModeChanged;

    _subscribed = false;
  }
}
