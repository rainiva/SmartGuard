using System.Text.RegularExpressions;

namespace SmartGuard.Configuration;

public sealed class LogRegexSpec
{
  public const int MaxPatternLength = 1024;
  public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

  private readonly Regex _regex;
  private readonly string _pattern;
  private readonly RegexOptions _options;

  public bool UsesBacktracking { get; }

  private LogRegexSpec(Regex regex, string pattern, RegexOptions options, bool usesBacktracking)
  {
    _regex = regex;
    _pattern = pattern;
    _options = options;
    UsesBacktracking = usesBacktracking;
  }

  public static LogRegexCompileResult TryCreate(string pattern, bool ignoreCase)
  {
    if (pattern.Length > MaxPatternLength)
      return LogRegexCompileResult.Failure($"Regex pattern exceeds {MaxPatternLength} characters.");

    var options = RegexOptions.CultureInvariant;
    if (ignoreCase)
      options |= RegexOptions.IgnoreCase;

    if (!ShouldUseBacktrackingEngine(pattern))
    {
      try
      {
        var regex = new Regex(pattern, options | RegexOptions.NonBacktracking, MatchTimeout);
        return LogRegexCompileResult.Success(new LogRegexSpec(regex, pattern, options, usesBacktracking: false));
      }
      catch (NotSupportedException)
      {
      }
      catch (ArgumentException ex)
      {
        return LogRegexCompileResult.Failure(ex.Message);
      }
    }

    try
    {
      var regex = new Regex(pattern, options, MatchTimeout);
      return LogRegexCompileResult.Success(new LogRegexSpec(regex, pattern, options, usesBacktracking: true));
    }
    catch (ArgumentException ex)
    {
      return LogRegexCompileResult.Failure(ex.Message);
    }
  }

  private static bool ShouldUseBacktrackingEngine(string pattern)
    => pattern.Contains(")+", StringComparison.Ordinal)
       || pattern.Contains(")*", StringComparison.Ordinal)
       || pattern.Contains("*+", StringComparison.Ordinal)
       || pattern.Contains("++", StringComparison.Ordinal);

  public bool IsMatch(string input)
    => _regex.IsMatch(input);

  public bool TryIsMatch(string input, CancellationToken cancellationToken, out bool timedOut)
  {
    timedOut = false;
    cancellationToken.ThrowIfCancellationRequested();

    if (!UsesBacktracking)
      return _regex.IsMatch(input);

    try
    {
      return Regex.IsMatch(input, _pattern, _options, MatchTimeout);
    }
    catch (RegexMatchTimeoutException)
    {
      timedOut = true;
      return false;
    }
  }
}
