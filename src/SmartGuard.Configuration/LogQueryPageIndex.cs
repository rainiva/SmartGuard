namespace SmartGuard.Configuration;

public sealed record LogQueryAnchor(
  int MatchOrdinal,
  LogReadCursor Cursor);

public sealed class LogQueryPageIndex
{
  public LogSourceSnapshot Snapshot { get; }
  public LogReadScope Scope { get; }
  public LogQueryExpression Expression { get; }
  public bool IgnoreCase { get; }
  public int ExactTotalCount { get; }
  public int ScannedCount { get; }
  public IReadOnlyList<LogQueryAnchor> Anchors { get; }

  internal LogQueryPageIndex(
    LogSourceSnapshot snapshot,
    LogReadScope scope,
    LogQueryExpression expression,
    bool ignoreCase,
    int exactTotalCount,
    int scannedCount,
    IReadOnlyList<LogQueryAnchor> anchors)
  {
    Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    Scope = scope ?? throw new ArgumentNullException(nameof(scope));
    Expression = expression ?? throw new ArgumentNullException(nameof(expression));
    IgnoreCase = ignoreCase;
    ExactTotalCount = exactTotalCount;
    ScannedCount = scannedCount;
    Anchors = anchors ?? throw new ArgumentNullException(nameof(anchors));
  }
}
