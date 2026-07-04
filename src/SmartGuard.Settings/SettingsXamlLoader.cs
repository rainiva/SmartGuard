using System.Windows;

namespace SmartGuard.Settings;

public static class SettingsXamlLoader
{
    private const string EmbeddedUri = "/SmartGuard.Settings;component/SmartGuard.Settings.xaml";

    public static Window? TryLoadEmbeddedWindow(out string? error)
    {
        error = null;
        try
        {
            return (Window)Application.LoadComponent(new Uri(EmbeddedUri, UriKind.Relative));
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }
}
