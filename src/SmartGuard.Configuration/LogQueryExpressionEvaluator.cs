using System.Globalization;

namespace SmartGuard.Configuration;

public static class LogQueryExpressionEvaluator
{
  public static bool Matches(LogEvent logEvent, LogQueryExpression expression)
    => Matches(logEvent, expression, context: null);

  public static bool Matches(
    LogEvent logEvent,
    LogQueryExpression expression,
    LogQueryEvaluationContext? context)
    => expression switch
    {
      LogQueryMatchAllExpression => true,
      LogQueryAndExpression and => Matches(logEvent, and.Left, context) && Matches(logEvent, and.Right, context),
      LogQueryOrExpression or => Matches(logEvent, or.Left, context) || Matches(logEvent, or.Right, context),
      LogQueryNotExpression not => !Matches(logEvent, not.Operand, context),
      LogQueryFieldExpression field => MatchesField(logEvent, field, context),
      _ => false,
    };

  public static bool Matches(LogIndexEntry entry, LogQueryExpression expression)
    => expression switch
    {
      LogQueryMatchAllExpression => true,
      LogQueryAndExpression and => Matches(entry, and.Left) && Matches(entry, and.Right),
      LogQueryOrExpression or => Matches(entry, or.Left) || Matches(entry, or.Right),
      LogQueryNotExpression not => !Matches(entry, not.Operand),
      LogQueryFieldExpression field => MatchesField(entry, field),
      _ => false,
    };

  private static bool MatchesField(
    LogEvent logEvent,
    LogQueryFieldExpression field,
    LogQueryEvaluationContext? context)
  {
    return field.Field switch
    {
      "severity" => CompareString(LogSeverityParser.ToToken(logEvent.Severity), field, context),
      "component" => CompareString(logEvent.Component, field, context),
      "channel" => CompareString(logEvent.Channel, field, context),
      "eventname" => CompareString(logEvent.EventName, field, context),
      "operationid" => CompareString(logEvent.OperationId, field, context),
      "traceid" => CompareString(logEvent.TraceId, field, context),
      "useractionid" => CompareString(logEvent.UserActionId, field, context),
      "processid" => CompareInt(logEvent.ProcessId, field),
      "threadid" => CompareInt(logEvent.ThreadId, field),
      "message" => CompareString(logEvent.Message, field, context),
      "time" => CompareTime(logEvent.Timestamp, field),
      _ => false,
    };
  }

  private static bool MatchesField(LogIndexEntry entry, LogQueryFieldExpression field)
  {
    return field.Field switch
    {
      "severity" => CompareString(entry.Level, field, context: null),
      "component" => CompareString(entry.Category, field, context: null),
      "channel" => CompareString(entry.Channel, field, context: null),
      "eventname" => CompareString(entry.EventName, field, context: null),
      "operationid" => CompareString(entry.OperationId, field, context: null),
      "traceid" => CompareString(entry.TraceId, field, context: null),
      "useractionid" => CompareString(entry.UserActionId, field, context: null),
      "processid" => CompareInt(entry.ProcessId, field),
      "threadid" => CompareInt(entry.ThreadId, field),
      "time" => CompareTime(entry.Timestamp, field),
      "message" => false,
      _ => false,
    };
  }

  private static bool CompareString(
    string? actual,
    LogQueryFieldExpression field,
    LogQueryEvaluationContext? context)
  {
    actual ??= string.Empty;
    return field.Operator switch
    {
      LogQueryOperator.Equals => actual.Equals(field.Value, StringComparison.OrdinalIgnoreCase),
      LogQueryOperator.NotEquals => !actual.Equals(field.Value, StringComparison.OrdinalIgnoreCase),
      LogQueryOperator.Contains => actual.Contains(field.Value, StringComparison.OrdinalIgnoreCase),
      LogQueryOperator.Regex => EvaluateRegex(actual, field.Value, context),
      LogQueryOperator.GreaterThan => string.Compare(actual, field.Value, StringComparison.OrdinalIgnoreCase) > 0,
      LogQueryOperator.GreaterThanOrEqual => string.Compare(actual, field.Value, StringComparison.OrdinalIgnoreCase) >= 0,
      LogQueryOperator.LessThan => string.Compare(actual, field.Value, StringComparison.OrdinalIgnoreCase) < 0,
      LogQueryOperator.LessThanOrEqual => string.Compare(actual, field.Value, StringComparison.OrdinalIgnoreCase) <= 0,
      _ => false,
    };
  }

  private static bool EvaluateRegex(
    string actual,
    string pattern,
    LogQueryEvaluationContext? context)
  {
    if (context is null)
      return LogRegexSpec.TryCreate(pattern, ignoreCase: true).Spec?.IsMatch(actual) == true;

    var regex = context.GetRegex(pattern);
    if (regex is null)
      return false;

    if (!regex.TryIsMatch(actual, CancellationToken.None, out var timedOut))
    {
      if (timedOut)
        context.MarkRegexTimedOut();
      return false;
    }

    return true;
  }

  private static bool CompareInt(int? actual, LogQueryFieldExpression field)
  {
    if (!int.TryParse(field.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expected))
      return false;

    if (actual is null)
      return false;

    return field.Operator switch
    {
      LogQueryOperator.Equals => actual.Value == expected,
      LogQueryOperator.NotEquals => actual.Value != expected,
      LogQueryOperator.GreaterThan => actual.Value > expected,
      LogQueryOperator.GreaterThanOrEqual => actual.Value >= expected,
      LogQueryOperator.LessThan => actual.Value < expected,
      LogQueryOperator.LessThanOrEqual => actual.Value <= expected,
      _ => false,
    };
  }

  private static bool CompareTime(DateTimeOffset actual, LogQueryFieldExpression field)
  {
    if (!DateTimeOffset.TryParse(field.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expected))
      return false;

    return field.Operator switch
    {
      LogQueryOperator.Equals => actual == expected,
      LogQueryOperator.NotEquals => actual != expected,
      LogQueryOperator.GreaterThan => actual > expected,
      LogQueryOperator.GreaterThanOrEqual => actual >= expected,
      LogQueryOperator.LessThan => actual < expected,
      LogQueryOperator.LessThanOrEqual => actual <= expected,
      _ => false,
    };
  }
}
