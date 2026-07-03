using System.Windows;
using System.Windows.Media;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

public static class AppBrandIcon
{
    internal static void ClearCacheForTests() => BrandIconLoader.ResetCacheForTests();

    public static ImageSource? LoadImageSource()
        => BrandIconLoader.LoadWpfImageSource();

    public static void ApplyTo(Window window)
    {
        var icon = LoadImageSource();
        if (icon is not null)
            window.Icon = icon;

        var image = window.FindName("imgAppIcon") as System.Windows.Controls.Image;
        if (image is not null && icon is not null)
            image.Source = icon;
    }
}
