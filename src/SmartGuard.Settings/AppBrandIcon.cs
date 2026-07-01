using System.Windows;
using System.Windows.Media;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

public static class AppBrandIcon
{
    internal static void ClearCacheForTests() => BrandIconLoader.ResetCacheForTests();

    public static ImageSource? LoadImageSource(string installRoot)
        => BrandIconLoader.LoadWpfImageSource(installRoot);

    public static void ApplyTo(Window window, string installRoot)
    {
        var icon = LoadImageSource(installRoot);
        if (icon is not null)
            window.Icon = icon;

        var image = window.FindName("imgAppIcon") as System.Windows.Controls.Image;
        if (image is not null && icon is not null)
            image.Source = icon;
    }
}
