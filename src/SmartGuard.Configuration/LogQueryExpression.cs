namespace SmartGuard.Configuration;

public abstract record LogQueryExpression
{
  public static LogQueryExpression AndAll(IReadOnlyList<LogQueryExpression> parts)
  {
    if (parts.Count == 0)
      throw new ArgumentException("At least one expression part is required.", nameof(parts));

    var current = parts[0];
    for (var i = 1; i < parts.Count; i++)
      current = new LogQueryAndExpression(current, parts[i]);

    return current;
  }
}

public sealed record LogQueryAndExpression(LogQueryExpression Left, LogQueryExpression Right) : LogQueryExpression;

public sealed record LogQueryOrExpression(LogQueryExpression Left, LogQueryExpression Right) : LogQueryExpression;

public sealed record LogQueryNotExpression(LogQueryExpression Operand) : LogQueryExpression;

public sealed record LogQueryFieldExpression(
  string Field,
  LogQueryOperator Operator,
  string Value) : LogQueryExpression;

public sealed record LogQueryMatchAllExpression : LogQueryExpression
{
  public static LogQueryMatchAllExpression Instance { get; } = new();
  private LogQueryMatchAllExpression() { }
}
