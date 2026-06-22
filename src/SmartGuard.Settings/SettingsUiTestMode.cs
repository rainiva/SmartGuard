namespace SmartGuard.Settings;

internal static class SettingsUiTestMode
{
    internal static bool IsEnabled { get; private set; }

    internal static void SetEnabled(bool enabled) => IsEnabled = enabled;
}
