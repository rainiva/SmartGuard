using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings;

public static class LogTagFilterCatalog
{
    public static IReadOnlyList<string> SelectableTags { get; } = ["INFO", "WARN", "ERROR", "HEART"];

    public static System.Drawing.Color GetTagColor(string tag)
        => LogViewerTagPalette.GetTagColor(tag);

    internal static LogQueryExpression BuildQueryExpression(string tag)
        => string.Equals(tag, "HEART", StringComparison.OrdinalIgnoreCase)
            ? new LogQueryOrExpression(
                new LogQueryFieldExpression("channel", LogQueryOperator.Contains, "HEART"),
                new LogQueryFieldExpression("message", LogQueryOperator.Contains, "[HEART]"))
            : new LogQueryFieldExpression("severity", LogQueryOperator.Equals, tag);
}
