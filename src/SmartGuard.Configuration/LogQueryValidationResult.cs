namespace SmartGuard.Configuration;

public sealed record LogQueryValidationResult(
  bool IsValid,
  LogQueryExpression? Expression,
  int? ErrorPosition,
  string? ErrorMessage)
{
  public static LogQueryValidationResult Success(LogQueryExpression expression)
    => new(true, expression, null, null);

  public static LogQueryValidationResult Failure(int position, string message)
    => new(false, null, position, message);
}
