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
    }
}
