namespace SmartGuard.Settings;

internal sealed record LogPagedMatchNavigationResult(int? RowIndex, int? TargetPage);

internal static class LogPagedMatchNavigator
{
  internal static LogPagedMatchNavigationResult Resolve(
    IReadOnlyList<LogTableRow> rows,
    string? keyword,
    int currentIndex,
    bool forward,
    LogPaginationState state)
  {
    ArgumentNullException.ThrowIfNull(rows);
    ArgumentNullException.ThrowIfNull(state);

    var start = forward
      ? Math.Max(-1, currentIndex) + 1
      : Math.Min(rows.Count, currentIndex < 0 ? rows.Count : currentIndex) - 1;
    var step = forward ? 1 : -1;
    var index = start;
    while (index >= 0 && index < rows.Count)
    {
      if (Matches(rows[index], keyword))
        return new LogPagedMatchNavigationResult(index, null);

      index += step;
    }

    int? targetPage = forward
      ? state.CanGoNext ? state.CurrentPage + 1 : null
      : state.CanGoPrevious ? state.CurrentPage - 1 : null;
    return new LogPagedMatchNavigationResult(null, targetPage);
  }

  private static bool Matches(LogTableRow row, string? keyword)
  {
    if (string.IsNullOrWhiteSpace(keyword))
      return true;

    return row.LevelText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
      || row.TimeText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
      || row.SourceText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
      || row.EventText.Contains(keyword, StringComparison.OrdinalIgnoreCase);
  }
}
