using System.Globalization;

namespace SmartGuard.Configuration;

public static class LogQueryExpressionEvaluator
{
  public static bool Matches(LogEvent logEvent, LogQueryExpression expression)
    => expression switch
    {
      LogQueryMatchAllExpression => true,
      LogQueryAndExpression and => Matches(logEvent, and.Left) && Matches(logEvent, and.Right),
      LogQueryOrExpression or => Matches(logEvent, or.Left) || Matches(logEvent, or.Right),
      LogQueryNotExpression not => !Matches(logEvent, not.Operand),
      LogQueryFieldExpression field => MatchesField(logEvent, field),
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

  private static bool MatchesField(LogEvent logEvent, LogQueryFieldExpression field)
  {
    return field.Field switch
    {
      "severity" => CompareString(LogSeverityParser.ToToken(logEvent.Severity), field),
      "component" => CompareString(logEvent.Component, field),
      "channel" => CompareString(logEvent.Channel, field),
      "eventname" => CompareString(logEvent.EventName, field),
      "operationid" => CompareString(logEvent.OperationId, field),
      "traceid" => CompareString(logEvent.TraceId, field),
      "useractionid" => CompareString(logEvent.UserActionId, field),
      "processid" => CompareInt(logEvent.ProcessId, field),
      "threadid" => CompareInt(logEvent.ThreadId, field),
      "message" => CompareString(logEvent.Message, field),
      "time" => CompareTime(logEvent.Timestamp, field),
      _ => false,
    };
  }

  private static bool MatchesField(LogIndexEntry entry, LogQueryFieldExpression field)
  {
    return field.Field switch
    {
      "severity" => CompareString(entry.Level, field),
      "component" => CompareString(entry.Category, field),
      "channel" => CompareString(entry.Channel, field),
      "eventname" => CompareString(entry.EventName, field),
      "operationid" => CompareString(entry.OperationId, field),
      "traceid" => CompareString(entry.TraceId, field),
      "useractionid" => CompareString(entry.UserActionId, field),
      "processid" => CompareInt(entry.ProcessId, field),
      "threadid" => CompareInt(entry.ThreadId, field),
      "time" => CompareTime(entry.Timestamp, field),
      "message" => false,
      _ => false,
    };
  }

  private static bool CompareString(string? actual, LogQueryFieldExpression field)
  {
    actual ??= string.Empty;
    return field.Operator switch
    {
      LogQueryOperator.Equals => actual.Equals(field.Value, StringComparison.OrdinalIgnoreCase),
      LogQueryOperator.NotEquals => !actual.Equals(field.Value, StringComparison.OrdinalIgnoreCase),
      LogQueryOperator.Contains => actual.Contains(field.Value, StringComparison.OrdinalIgnoreCase),
      LogQueryOperator.GreaterThan => string.Compare(actual, field.Value, StringComparison.OrdinalIgnoreCase) > 0,
      LogQueryOperator.GreaterThanOrEqual => string.Compare(actual, field.Value, StringComparison.OrdinalIgnoreCase) >= 0,
      LogQueryOperator.LessThan => string.Compare(actual, field.Value, StringComparison.OrdinalIgnoreCase) < 0,
      LogQueryOperator.LessThanOrEqual => string.Compare(actual, field.Value, StringComparison.OrdinalIgnoreCase) <= 0,
      _ => false,
    };
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
