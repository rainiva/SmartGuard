namespace SmartGuard.Configuration;

public enum LogCorrelationKeyKind
{
  OperationId,
  TraceId,
  UserActionId,
}

public sealed record LogOperationCorrelationKey(LogCorrelationKeyKind Kind, string Value);

public static class LogOperationGraph
{
  public static LogOperationCorrelationKey? ResolveCorrelationKey(LogEvent logEvent)
  {
    if (!string.IsNullOrWhiteSpace(logEvent.OperationId))
      return new LogOperationCorrelationKey(LogCorrelationKeyKind.OperationId, logEvent.OperationId);

    if (!string.IsNullOrWhiteSpace(logEvent.TraceId))
      return new LogOperationCorrelationKey(LogCorrelationKeyKind.TraceId, logEvent.TraceId);

    if (!string.IsNullOrWhiteSpace(logEvent.UserActionId))
      return new LogOperationCorrelationKey(LogCorrelationKeyKind.UserActionId, logEvent.UserActionId);

    return null;
  }

  public static LogQueryExpression BuildCorrelationExpression(LogOperationCorrelationKey key)
  {
    var field = key.Kind switch
    {
      LogCorrelationKeyKind.OperationId => "operationid",
      LogCorrelationKeyKind.TraceId => "traceid",
      LogCorrelationKeyKind.UserActionId => "useractionid",
      _ => throw new ArgumentOutOfRangeException(nameof(key), key.Kind, "Unsupported correlation key kind."),
    };

    return new LogQueryFieldExpression(field, LogQueryOperator.Equals, key.Value);
  }

  public static LogQueryExpression CombineWithBase(LogQueryExpression? baseExpression, LogQueryExpression correlationClause)
  {
    if (baseExpression is null or LogQueryMatchAllExpression)
      return correlationClause;

    return new LogQueryAndExpression(baseExpression, correlationClause);
  }
}
