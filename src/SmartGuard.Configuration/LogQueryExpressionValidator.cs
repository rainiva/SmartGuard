namespace SmartGuard.Configuration;

public static class LogQueryExpressionValidator
{
  public static LogQueryValidationResult Validate(LogQueryExpression expression, string originalText)
  {
    if (originalText.Length > LogQueryLimits.MaxExpressionLength)
    {
      return LogQueryValidationResult.Failure(
        0,
        $"Query expression exceeds {LogQueryLimits.MaxExpressionLength} characters.");
    }

    if (MeasureDepth(expression) > LogQueryLimits.MaxAstDepth)
    {
      return LogQueryValidationResult.Failure(
        0,
        $"Query AST depth exceeds {LogQueryLimits.MaxAstDepth}.");
    }

    foreach (var field in EnumerateFieldExpressions(expression))
    {
      if (field.Operator != LogQueryOperator.Regex)
        continue;

      if (field.Value.Length > LogRegexSpec.MaxPatternLength)
      {
        return LogQueryValidationResult.Failure(
          0,
          $"Regex pattern exceeds {LogRegexSpec.MaxPatternLength} characters.");
      }
    }

    return LogQueryValidationResult.Success(expression);
  }

  private static int MeasureDepth(LogQueryExpression expression)
    => expression switch
    {
      LogQueryAndExpression and => 1 + Math.Max(MeasureDepth(and.Left), MeasureDepth(and.Right)),
      LogQueryOrExpression or => 1 + Math.Max(MeasureDepth(or.Left), MeasureDepth(or.Right)),
      LogQueryNotExpression not => 1 + MeasureDepth(not.Operand),
      _ => 1,
    };

  private static IEnumerable<LogQueryFieldExpression> EnumerateFieldExpressions(LogQueryExpression expression)
  {
    switch (expression)
    {
      case LogQueryAndExpression and:
        foreach (var part in EnumerateFieldExpressions(and.Left))
          yield return part;
        foreach (var part in EnumerateFieldExpressions(and.Right))
          yield return part;
        yield break;
      case LogQueryOrExpression or:
        foreach (var part in EnumerateFieldExpressions(or.Left))
          yield return part;
        foreach (var part in EnumerateFieldExpressions(or.Right))
          yield return part;
        yield break;
      case LogQueryNotExpression not:
        foreach (var part in EnumerateFieldExpressions(not.Operand))
          yield return part;
        yield break;
      case LogQueryFieldExpression field:
        yield return field;
        yield break;
    }
  }
}
