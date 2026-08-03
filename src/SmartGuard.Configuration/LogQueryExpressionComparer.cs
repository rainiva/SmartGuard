namespace SmartGuard.Configuration;

internal static class LogQueryExpressionComparer
{
  public static bool AreEquivalent(LogQueryExpression left, LogQueryExpression right)
    => Normalize(left) == Normalize(right);

  internal static string ToCanonicalText(LogQueryExpression expression) => Normalize(expression);

  private static string Normalize(LogQueryExpression expression) => expression switch
  {
    LogQueryAndExpression and => $"AND({Normalize(and.Left)},{Normalize(and.Right)})",
    LogQueryOrExpression or => $"OR({Normalize(or.Left)},{Normalize(or.Right)})",
    LogQueryNotExpression not => $"NOT({Normalize(not.Operand)})",
    LogQueryFieldExpression field => $"{field.Field}:{field.Operator}:{field.Value}",
    _ => expression.ToString() ?? string.Empty,
  };
}
