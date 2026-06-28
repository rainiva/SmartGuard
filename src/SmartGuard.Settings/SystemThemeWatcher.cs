using Microsoft.Win32;

namespace SmartGuard.Settings;

public sealed class SystemThemeWatcher : IDisposable
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValueName = "AppsUseLightTheme";

    internal static Func<int?>? RegistryReaderForTests;

    public event EventHandler? SystemThemeChanged;

    public SystemThemeWatcher()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public static bool IsSystemDarkMode()
    {
        var appsUseLightTheme = RegistryReaderForTests?.Invoke() ?? ReadAppsUseLightThemeFromRegistry();
        return appsUseLightTheme == 0;
    }

    internal static void ResetForTests()
    {
        RegistryReaderForTests = null;
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
            SystemThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static int? ReadAppsUseLightThemeFromRegistry()
    {
        var value = Registry.GetValue(
            $@"HKEY_CURRENT_USER\{PersonalizeKeyPath}",
            AppsUseLightThemeValueName,
            null);

        return value switch
        {
            null => null,
            int intValue => intValue,
            _ => Convert.ToInt32(value),
        };
    }
}
