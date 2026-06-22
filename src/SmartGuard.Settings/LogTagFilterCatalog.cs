using SmartGuard.LogViewer;

namespace SmartGuard.Settings;

public static class LogTagFilterCatalog
{
    public static IReadOnlyList<string> SelectableTags { get; } = ["INFO", "WARN", "ERROR", "HEART"];

    public static System.Drawing.Color GetTagColor(string tag)
        => LogViewerTagPalette.GetTagColor(tag);
}
