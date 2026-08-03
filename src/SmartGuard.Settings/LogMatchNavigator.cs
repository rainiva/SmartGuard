using SmartGuard.Configuration;

namespace SmartGuard.Settings;

public enum LogMatchNavigationDirection
{
  Forward,
  Backward,
}

public sealed record LogMatchNavigationResult(
  string? TargetEventId,
  bool RequiresPageLoad,
  LogQueryCursor? TargetCursor,
  int CurrentIndex);

public sealed class LogMatchNavigator
{
  private int _queryGeneration;
  private int _currentMatchIndex;
  private IReadOnlyList<LogMatch> _matches = Array.Empty<LogMatch>();
  private HashSet<string> _visibleEventIds = new(StringComparer.Ordinal);

  public IReadOnlyList<LogMatch> CurrentMatches => _matches;

  public int CurrentMatchIndex => _currentMatchIndex;

  public void NotifyQueryGenerationChanged(int queryGeneration)
  {
    if (queryGeneration == _queryGeneration)
      return;

    _queryGeneration = queryGeneration;
    _currentMatchIndex = 0;
    _matches = Array.Empty<LogMatch>();
    _visibleEventIds.Clear();
  }

  public void SetMatches(
    int queryGeneration,
    IReadOnlyList<LogMatch> matches,
    bool scanComplete,
    IEnumerable<string> visibleEventIds)
  {
    _ = scanComplete;
    _queryGeneration = queryGeneration;
    _matches = matches;
    _visibleEventIds = visibleEventIds.ToHashSet(StringComparer.Ordinal);
    if (_currentMatchIndex > _matches.Count)
      _currentMatchIndex = 0;
  }

  public LogMatchNavigationResult NavigateNext(bool reverse)
  {
    if (_matches.Count == 0)
      return new LogMatchNavigationResult(null, false, null, 0);

    if (_currentMatchIndex == 0)
      _currentMatchIndex = reverse ? _matches.Count : 1;
    else if (reverse)
      _currentMatchIndex = _currentMatchIndex <= 1 ? _matches.Count : _currentMatchIndex - 1;
    else
      _currentMatchIndex = _currentMatchIndex >= _matches.Count ? 1 : _currentMatchIndex + 1;

    var match = _matches[_currentMatchIndex - 1];
    var visible = _visibleEventIds.Contains(match.EventId);
    return new LogMatchNavigationResult(
      match.EventId,
      RequiresPageLoad: !visible,
      visible ? null : new LogQueryCursor(match.Source, match.ByteOffset, match.EventId, match.Ordinal),
      _currentMatchIndex);
  }

  public static string BuildStatusText(int currentIndex, int discoveredCount, bool scanComplete)
  {
    if (discoveredCount == 0)
      return "零匹配";

    if (!scanComplete)
    {
      return currentIndex > 0
        ? $"第 {currentIndex} 个 / 已发现 {discoveredCount}+"
        : $"已发现 {discoveredCount}+";
    }

    return currentIndex > 0
      ? $"第 {currentIndex} 个 / {discoveredCount}"
      : $"{discoveredCount} 个匹配";
  }
}
