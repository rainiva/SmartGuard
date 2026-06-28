using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class SystemThemeWatcherTests
{
    [Fact]
    public void IsSystemDarkMode_returns_true_when_apps_use_light_theme_is_zero()
    {
        SystemThemeWatcher.RegistryReaderForTests = () => 0;
        try
        {
            SystemThemeWatcher.IsSystemDarkMode().Should().BeTrue();
        }
        finally
        {
            SystemThemeWatcher.ResetForTests();
        }
    }

    [Fact]
    public void IsSystemDarkMode_returns_false_when_apps_use_light_theme_is_one()
    {
        SystemThemeWatcher.RegistryReaderForTests = () => 1;
        try
        {
            SystemThemeWatcher.IsSystemDarkMode().Should().BeFalse();
        }
        finally
        {
            SystemThemeWatcher.ResetForTests();
        }
    }

    [Fact]
    public void IsSystemDarkMode_defaults_to_light_when_registry_value_missing()
    {
        SystemThemeWatcher.RegistryReaderForTests = () => null;
        try
        {
            SystemThemeWatcher.IsSystemDarkMode().Should().BeFalse();
        }
        finally
        {
            SystemThemeWatcher.ResetForTests();
        }
    }
}
