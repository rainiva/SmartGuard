using System.Windows;

namespace SmartGuard.Settings;

public static class SettingsWindowPresentation
{
    public static void RegisterShowHooks(Window window)
    {
        void OnLoaded(object? sender, RoutedEventArgs e)
        {
            window.Loaded -= OnLoaded;
            BringToForeground(window);
        }

        window.Loaded += OnLoaded;
    }

    public static void BringToForeground(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }
}
