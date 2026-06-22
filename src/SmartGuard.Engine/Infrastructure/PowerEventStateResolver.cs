using Microsoft.Win32;

namespace SmartGuard.Engine.Infrastructure;

public static class PowerEventStateResolver
{
    public static bool Resolve(PowerModes mode, Func<(int Percent, bool IsOnAc)> readBatteryInfo)
    {
        var interpreted = PowerEventInterpreter.InterpretPowerMode(mode);
        if (interpreted is bool value)
            return value;

        return readBatteryInfo().IsOnAc;
    }
}
