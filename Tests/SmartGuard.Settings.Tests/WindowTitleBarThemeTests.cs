using System.Windows;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class WindowTitleBarThemeTests
{
    [Fact]
    public void GetLastRequestedDarkMode_tracks_each_window_independently()
    {
        WpfStaTestHost.Run(() =>
        {
            var lightWindow = new Window();
            var darkWindow = new Window();

            WindowTitleBarTheme.Apply(lightWindow, isDarkMode: false);
            WindowTitleBarTheme.Apply(darkWindow, isDarkMode: true);

            WindowTitleBarTheme.GetLastRequestedDarkMode(lightWindow).Should().BeFalse();
            WindowTitleBarTheme.GetLastRequestedDarkMode(darkWindow).Should().BeTrue();
        });
    }
}
