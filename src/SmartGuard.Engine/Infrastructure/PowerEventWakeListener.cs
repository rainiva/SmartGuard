using Microsoft.Win32;

namespace SmartGuard.Engine.Infrastructure;

public sealed class PowerEventWakeListener : IDisposable
{
  private readonly Action<bool> _onPowerChanged;

  public PowerEventWakeListener(Action<bool> onPowerChanged)
  {
    _onPowerChanged = onPowerChanged;
    SystemEvents.PowerModeChanged += OnPowerModeChanged;
  }

  private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
  {
    var isOnAc = PowerEventStateResolver.Resolve(
      e.Mode,
      readBatteryInfo: () => BatteryInfoProvider.GetBatteryInfo(forceRefresh: true));
    _onPowerChanged(isOnAc);
  }

  public void Dispose() =>
    SystemEvents.PowerModeChanged -= OnPowerModeChanged;
}
