using System.IO;

namespace SmartGuard.Settings;

public static class LogViewStatusTextBuilder
{
    public static string Build(LogViewSnapshot snapshot, DateTime refreshedAt)
    {
        var truncationHint = snapshot.IsTailTruncated ? " | 仅最近 256KB" : string.Empty;
        var searchHint = snapshot.HasSearchKeyword || snapshot.HasActiveTimeFilter
            ? $" | 匹配 {snapshot.FilteredLines.Count} 条"
            : string.Empty;
        var logName = Path.GetFileName(snapshot.LogPath);
        return $"显示 {snapshot.FilteredLines.Count} / 总计 {snapshot.TotalLineCount} 行{truncationHint}{searchHint} | {logName} | 刷新: {refreshedAt:HH:mm:ss}";
    }
}
