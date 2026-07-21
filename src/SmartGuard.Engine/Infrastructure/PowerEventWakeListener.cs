using Microsoft.Win32;

namespace SmartGuard.Engine.Infrastructure;

public sealed class PowerEventWakeListener : IDisposable
{
  private readonly Action<bool> _onPowerChanged;
  private bool _subscribed;
  private bool _usedTestOverride;

  internal static Action<PowerModeChangedEventHandler>? SubscribeOverrideForTests;
  internal static Action<PowerModeChangedEventHandler>? UnsubscribeOverrideForTests;

  public bool IsEnabled { get; private set; }

  public PowerEventWakeListener(Action<bool> onPowerChanged)
    : this(onPowerChanged, subscribe: true)
  {
  }

  private PowerEventWakeListener(Action<bool> onPowerChanged, bool subscribe)
  {
    _onPowerChanged = onPowerChanged;
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

  public static PowerEventWakeListener Create(Action<bool> onPowerChanged)
    => new(onPowerChanged, subscribe: true);

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
    var isOnAc = PowerEventStateResolver.Resolve(
      e.Mode,
      readBatteryInfo: () => BatteryInfoProvider.GetBatteryInfo(forceRefresh: true));
    _onPowerChanged(isOnAc);
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
