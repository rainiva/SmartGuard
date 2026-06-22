namespace SmartGuard.Settings;

public sealed record LogViewSnapshot(
    IReadOnlyList<string> FilteredLines,
    int TotalLineCount,
    bool IsTailTruncated,
    string LogPath,
    bool ContentChanged,
    string SearchKeyword = "",
    string? EmptyStateMessage = null,
    LogViewTimeRange TimeRange = LogViewTimeRange.All,
    int MatchedLineCount = 0,
    bool IsDisplayTruncated = false,
    IReadOnlyList<string>? ActiveTagFilters = null)
{
    public bool HasSearchKeyword => !string.IsNullOrWhiteSpace(SearchKeyword);

    public bool HasActiveTimeFilter => TimeRange != LogViewTimeRange.All;

    public IReadOnlyList<string> TagFilters => ActiveTagFilters ?? [];

    public bool HasActiveTagFilter => TagFilters.Count > 0;

    public int EffectiveMatchedCount => MatchedLineCount > 0 ? MatchedLineCount : FilteredLines.Count;

    public IReadOnlyList<string> DisplayLines =>
        EmptyStateMessage is null ? FilteredLines : [EmptyStateMessage];
}
