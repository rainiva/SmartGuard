namespace SmartGuard.Settings;

public sealed record LogViewSnapshot(
    IReadOnlyList<string> FilteredLines,
    int TotalLineCount,
    bool IsTailTruncated,
    string LogPath,
    bool ContentChanged,
    string SearchKeyword = "",
    string? EmptyStateMessage = null,
    LogViewTimeRange TimeRange = LogViewTimeRange.All)
{
    public bool HasSearchKeyword => !string.IsNullOrWhiteSpace(SearchKeyword);

    public bool HasActiveTimeFilter => TimeRange != LogViewTimeRange.All;

    public IReadOnlyList<string> DisplayLines =>
        EmptyStateMessage is null ? FilteredLines : [EmptyStateMessage];
}
