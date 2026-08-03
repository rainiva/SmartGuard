namespace SmartGuard.Configuration;

public static class LogOperationProjector
{
  public static IReadOnlyList<LogEvent> OrderCorrelatedEvents(IEnumerable<LogEvent> events)
    => events
      .OrderBy(logEvent => logEvent.Timestamp)
      .ThenBy(logEvent => logEvent.EventId, StringComparer.Ordinal)
      .ToList();

  public static string BuildContextTitle(LogOperationCorrelationKey key)
    => key.Kind switch
    {
      LogCorrelationKeyKind.OperationId => $"操作关联：{key.Value}",
      LogCorrelationKeyKind.TraceId => $"Trace 关联：{key.Value}",
      LogCorrelationKeyKind.UserActionId => $"用户操作关联：{key.Value}",
      _ => "关联视图",
    };
}
