namespace SmartGuard.Configuration;

public sealed record LogRegexCompileResult(
  bool IsValid,
  LogRegexSpec? Spec,
  string? ErrorMessage)
{
  public static LogRegexCompileResult Success(LogRegexSpec spec) => new(true, spec, null);

  public static LogRegexCompileResult Failure(string message) => new(false, null, message);
}
