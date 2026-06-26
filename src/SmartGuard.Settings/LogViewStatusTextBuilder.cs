using System.IO;

namespace SmartGuard.Settings;

public static class LogViewStatusTextBuilder
{
    public static string Build(
        LogViewSnapshot snapshot,
        DateTime refreshedAt,
        int? idleSeconds = null,
        bool statusMayBeStale = false)
    {
        var truncationHint = snapshot.IsTailTruncated ? " | 仅最近 256KB" : string.Empty;
        var displayHint = snapshot.IsDisplayTruncated
            ? $" | 渲染 {snapshot.FilteredLines.Count}/{snapshot.EffectiveMatchedCount} 条"
            : string.Empty;
        var searchHint = snapshot.HasSearchKeyword || snapshot.HasActiveTimeFilter || snapshot.HasActiveTagFilter
            ? $" | 匹配 {snapshot.EffectiveMatchedCount} 条"
            : string.Empty;
        var idleHint = idleSeconds is int idle ? $" | 当前空闲 {idle} 秒" : string.Empty;
        var staleHint = statusMayBeStale ? " | 状态可能过期" : string.Empty;
        var logName = Path.GetFileName(snapshot.LogPath);
        return $"显示 {snapshot.FilteredLines.Count} / 总计 {snapshot.TotalLineCount} 行{truncationHint}{displayHint}{searchHint}{idleHint}{staleHint} | {logName} | 刷新: {refreshedAt:HH:mm:ss}";
    }
}
