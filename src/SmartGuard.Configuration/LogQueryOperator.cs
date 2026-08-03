namespace SmartGuard.Configuration;

public enum LogQueryOperator
{
  Equals,
  NotEquals,
  Contains,
  GreaterThan,
  GreaterThanOrEqual,
  LessThan,
  LessThanOrEqual,
}

public static class LogQueryOperatorTokens
{
  public static LogQueryOperator FromToken(string token) => token switch
  {
    ":" or "=" => LogQueryOperator.Equals,
    "!=" => LogQueryOperator.NotEquals,
    "~" => LogQueryOperator.Contains,
    ">" => LogQueryOperator.GreaterThan,
    ">=" => LogQueryOperator.GreaterThanOrEqual,
    "<" => LogQueryOperator.LessThan,
    "<=" => LogQueryOperator.LessThanOrEqual,
    _ => throw new ArgumentOutOfRangeException(nameof(token), token, "Unknown query operator."),
  };
}
