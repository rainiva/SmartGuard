using System.Windows.Media;
using SmartGuard.LogViewer;
using MediaColor = System.Windows.Media.Color;

namespace SmartGuard.Settings;

public static class LogViewTagPalette
{
    private static bool _isDarkMode;
    private static readonly Dictionary<string, SolidColorBrush> TagBrushes = new(StringComparer.OrdinalIgnoreCase);
    private static SolidColorBrush? _bodyBrush;

    public static void ConfigureForDarkMode(bool isDarkMode)
    {
        if (_isDarkMode == isDarkMode && TagBrushes.Count > 0)
            return;

        _isDarkMode = isDarkMode;
        ClearCache();
    }

    public static SolidColorBrush GetBodyBrush()
    {
        _bodyBrush ??= CreateFrozenBrush(GetBodyColor());
        return _bodyBrush;
    }

    public static MediaColor GetBodyColor()
        => _isDarkMode
            ? MediaColor.FromRgb(0xE8, 0xE8, 0xE8)
            : MediaColor.FromRgb(30, 30, 30);

    public static SolidColorBrush GetTagBrush(string tag)
    {
        var key = tag.ToUpperInvariant();
        if (TagBrushes.TryGetValue(key, out var cached))
            return cached;

        var brush = key switch
        {
            "RAW" => CreateFrozenBrush(_isDarkMode
                ? MediaColor.FromRgb(0xA8, 0xA8, 0xA8)
                : MediaColor.FromRgb(120, 120, 120)),
            _ => CreateFrozenBrush(ToMediaColor(LogViewerTagPalette.GetTagColor(tag))),
        };
        TagBrushes[key] = brush;
        return brush;
    }

    private static void ClearCache()
    {
        TagBrushes.Clear();
        _bodyBrush = null;
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
