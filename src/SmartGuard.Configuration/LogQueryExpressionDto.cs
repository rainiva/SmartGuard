using System.Text.Json.Serialization;

namespace SmartGuard.Configuration;

public sealed class LogQueryExpressionDto
{
  public const int CurrentSchemaVersion = 1;

  public int SchemaVersion { get; set; } = CurrentSchemaVersion;

  public string Kind { get; set; } = "matchAll";

  public LogQueryExpressionDto? Left { get; set; }

  public LogQueryExpressionDto? Right { get; set; }

  public LogQueryExpressionDto? Operand { get; set; }

  public string? Field { get; set; }

  public string? Operator { get; set; }

  public string? Value { get; set; }

  public static LogQueryExpressionDto MatchAll() => new() { Kind = "matchAll" };

  public static LogQueryExpressionDto FromExpression(LogQueryExpression expression) => expression switch
  {
    LogQueryAndExpression and => new LogQueryExpressionDto
    {
      Kind = "and",
      Left = FromExpression(and.Left),
      Right = FromExpression(and.Right),
    },
    LogQueryOrExpression or => new LogQueryExpressionDto
    {
      Kind = "or",
      Left = FromExpression(or.Left),
      Right = FromExpression(or.Right),
    },
    LogQueryNotExpression not => new LogQueryExpressionDto
    {
      Kind = "not",
      Operand = FromExpression(not.Operand),
    },
    LogQueryFieldExpression field => new LogQueryExpressionDto
    {
      Kind = "field",
      Field = field.Field,
      Operator = field.Operator.ToString(),
      Value = field.Value,
    },
    LogQueryMatchAllExpression => MatchAll(),
    _ => MatchAll(),
  };

  public LogQueryExpression ToExpression() => Kind switch
  {
    "and" => new LogQueryAndExpression(
      RequiredChild(Left, nameof(Left)).ToExpression(),
      RequiredChild(Right, nameof(Right)).ToExpression()),
    "or" => new LogQueryOrExpression(
      RequiredChild(Left, nameof(Left)).ToExpression(),
      RequiredChild(Right, nameof(Right)).ToExpression()),
    "not" => new LogQueryNotExpression(RequiredChild(Operand, nameof(Operand)).ToExpression()),
    "field" => new LogQueryFieldExpression(
      Field ?? string.Empty,
      Enum.TryParse<LogQueryOperator>(Operator, ignoreCase: true, out var parsedOperator)
        ? parsedOperator
        : LogQueryOperator.Equals,
      Value ?? string.Empty),
    _ => LogQueryMatchAllExpression.Instance,
  };

  private static LogQueryExpressionDto RequiredChild(LogQueryExpressionDto? child, string name)
    => child ?? throw new InvalidOperationException($"Missing {name} for {name} expression DTO.");
}
