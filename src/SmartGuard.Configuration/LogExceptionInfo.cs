namespace SmartGuard.Configuration;

public sealed record LogExceptionInfo(
  string Type,
  string Message,
  string? StackTrace,
  LogExceptionInfo? Inner)
{
  public int CountInners()
  {
    var count = 0;
    for (var inner = Inner; inner is not null; inner = inner.Inner)
      count++;
    return count;
  }

  public IEnumerable<string> FormatExpandedLines()
  {
    yield return $"{Type}: {Message}";
    if (!string.IsNullOrWhiteSpace(StackTrace))
      yield return StackTrace;

    if (Inner is not null)
    {
      yield return "---> InnerException:";
      foreach (var line in Inner.FormatExpandedLines())
        yield return line;
    }
  }
}
