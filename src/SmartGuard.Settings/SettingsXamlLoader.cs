using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace SmartGuard.Settings;

public static class SettingsXamlLoader
{
    private const string EmbeddedUri = "/SmartGuard.Settings;component/SmartGuard.Settings.xaml";

    public static string PrepareLooseXamlForParse(string xaml) =>
        xaml.Replace(
            "xmlns:local=\"clr-namespace:SmartGuard.Settings\"",
            "xmlns:local=\"clr-namespace:SmartGuard.Settings;assembly=SmartGuard.Settings\"");

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

    public static Window? LoadLooseWindow(string xaml)
    {
        var prepared = PrepareLooseXamlForParse(xaml);
        return (Window)XamlReader.Parse(prepared);
    }

    public static Window? TryLoadLooseWindowFromFile(string xamlPath, out string? error)
    {
        error = null;
        if (!File.Exists(xamlPath))
        {
            error = $"Missing settings layout file: {xamlPath}";
            return null;
        }

        try
        {
            return LoadLooseWindow(File.ReadAllText(xamlPath));
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }
}
