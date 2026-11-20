namespace SmartGuard.Configuration;

public sealed class LogQueryEvaluationContext
{
  private readonly bool _ignoreCase;
  private readonly Dictionary<string, LogRegexSpec?> _regexCache = new(StringComparer.Ordinal);

  public bool RegexTimedOut { get; private set; }

  public LogQueryEvaluationContext(bool ignoreCase) => _ignoreCase = ignoreCase;

  public void ResetTimeout() => RegexTimedOut = false;

  internal LogRegexSpec? GetRegex(string pattern)
  {
    if (!_regexCache.TryGetValue(pattern, out var spec))
    {
      var compiled = LogRegexSpec.TryCreate(pattern, _ignoreCase);
      spec = compiled.IsValid ? compiled.Spec : null;
      _regexCache[pattern] = spec;
    }

    return spec;
  }

  internal void MarkRegexTimedOut() => RegexTimedOut = true;
}
