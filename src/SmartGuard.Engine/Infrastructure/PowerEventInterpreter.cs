using Microsoft.Win32;

namespace SmartGuard.Engine.Infrastructure;

public static class PowerEventInterpreter
{
  public static bool? InterpretPowerMode(PowerModes mode) =>
    mode switch
    {
      PowerModes.Resume => true,
      PowerModes.Suspend => false,
      _ => null,
    };
}
