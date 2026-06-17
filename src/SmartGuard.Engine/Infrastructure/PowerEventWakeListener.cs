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
    var isOnAc = PowerEventInterpreter.InterpretPowerMode(e.Mode)
      ?? BatteryInfoProvider.GetBatteryInfo().IsOnAc;
    _onPowerChanged(isOnAc);
  }

  public void Dispose() =>
    SystemEvents.PowerModeChanged -= OnPowerModeChanged;
}
