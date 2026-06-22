using System.Runtime.CompilerServices;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

internal static class SettingsUiTestModeBootstrap
{
    [ModuleInitializer]
    internal static void Enable()
        => SettingsUiTestMode.SetEnabled(true);
}
