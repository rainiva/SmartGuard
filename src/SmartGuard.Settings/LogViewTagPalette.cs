using System.Windows.Media;
using SmartGuard.LogViewer;
using MediaColor = System.Windows.Media.Color;

namespace SmartGuard.Settings;

public static class LogViewTagPalette
{
    private static bool _isDarkMode;

    public static void ConfigureForDarkMode(bool isDarkMode) => _isDarkMode = isDarkMode;

    public static SolidColorBrush GetBodyBrush() => CreateFrozenBrush(GetBodyColor());

    public static MediaColor GetBodyColor()
        => _isDarkMode
            ? MediaColor.FromRgb(0xE8, 0xE8, 0xE8)
            : MediaColor.FromRgb(30, 30, 30);

    public static SolidColorBrush GetTagBrush(string tag)
    {
        return tag.ToUpperInvariant() switch
        {
            "RAW" => CreateFrozenBrush(_isDarkMode
                ? MediaColor.FromRgb(0xA8, 0xA8, 0xA8)
                : MediaColor.FromRgb(120, 120, 120)),
            _ => CreateFrozenBrush(ToMediaColor(LogViewerTagPalette.GetTagColor(tag))),
        };
    }

    private static MediaColor ToMediaColor(System.Drawing.Color color)
        => MediaColor.FromArgb(color.A, color.R, color.G, color.B);

    private static SolidColorBrush CreateFrozenBrush(MediaColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
