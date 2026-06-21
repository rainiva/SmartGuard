using System.Windows.Media;
using SmartGuard.LogViewer;

namespace SmartGuard.Settings;

public static class LogViewTagPalette
{
    public static SolidColorBrush BodyBrush { get; } = CreateFrozenBrush(LogViewerTagPalette.BodyColor);

    public static SolidColorBrush GetTagBrush(string tag)
    {
        return tag.ToUpperInvariant() switch
        {
            "RAW" => CreateFrozenBrush(System.Drawing.Color.FromArgb(120, 120, 120)),
            _ => CreateFrozenBrush(LogViewerTagPalette.GetTagColor(tag)),
        };
    }

    private static SolidColorBrush CreateFrozenBrush(System.Drawing.Color color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
