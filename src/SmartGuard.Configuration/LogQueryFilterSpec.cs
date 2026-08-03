namespace SmartGuard.Configuration;

/// <summary>
/// 结构化筛选规格 record：所有常用筛选字段集中管理。
/// 参考标准文档 §23 UI 查询：时间/等级/模块/事件/OperationId/关键词/PID/TID。
/// </summary>
public sealed record LogQueryFilterSpec(
  string? Keyword,
  string? Level,
  string? Category,
  string? OperationId,
  string? TraceId,
  int? ProcessId,
  int? ThreadId,
  string? EventName,
  DateTimeOffset? StartTime,
  DateTimeOffset? EndTime,
  int Page,
  int PageSize,
  string? TextExpression = null)
{
  public static LogQueryFilterSpec Default =>
    new(null, null, null, null, null, null, null, null, null, null, 1, 50);

  public LogQueryRequest ToQueryRequest() => new(
    Keyword, Level, Category, OperationId, TraceId,
    ProcessId, ThreadId, EventName, Page, PageSize, TextExpression);

  public LogQueryExpression ToExpression()
  {
    if (!string.IsNullOrWhiteSpace(TextExpression))
    {
      var parsed = LogQueryParser.Parse(TextExpression);
      if (parsed.IsValid && parsed.Expression is not null)
        return parsed.Expression;
    }

    var parts = new List<LogQueryExpression>();
    if (!string.IsNullOrWhiteSpace(Keyword))
    {
      parts.Add(new LogQueryOrExpression(
        new LogQueryFieldExpression("message", LogQueryOperator.Contains, Keyword),
        new LogQueryOrExpression(
          new LogQueryFieldExpression("eventname", LogQueryOperator.Contains, Keyword),
          new LogQueryOrExpression(
            new LogQueryFieldExpression("operationid", LogQueryOperator.Contains, Keyword),
            new LogQueryFieldExpression("component", LogQueryOperator.Contains, Keyword)))));
    }

    if (!string.IsNullOrWhiteSpace(Level))
      parts.Add(new LogQueryFieldExpression("severity", LogQueryOperator.Equals, Level));

    if (!string.IsNullOrWhiteSpace(Category))
      parts.Add(new LogQueryFieldExpression("component", LogQueryOperator.Equals, Category));

    if (!string.IsNullOrWhiteSpace(OperationId))
      parts.Add(new LogQueryFieldExpression("operationid", LogQueryOperator.Equals, OperationId));

    if (!string.IsNullOrWhiteSpace(TraceId))
      parts.Add(new LogQueryFieldExpression("traceid", LogQueryOperator.Equals, TraceId));

    if (ProcessId is int processId)
      parts.Add(new LogQueryFieldExpression("processid", LogQueryOperator.Equals, processId.ToString()));

    if (ThreadId is int threadId)
      parts.Add(new LogQueryFieldExpression("threadid", LogQueryOperator.Equals, threadId.ToString()));

    if (!string.IsNullOrWhiteSpace(EventName))
      parts.Add(new LogQueryFieldExpression("eventname", LogQueryOperator.Equals, EventName));

    if (StartTime is DateTimeOffset startTime)
    {
      parts.Add(new LogQueryFieldExpression(
        "time", LogQueryOperator.GreaterThanOrEqual, startTime.ToString("O")));
    }

    if (EndTime is DateTimeOffset endTime)
    {
      parts.Add(new LogQueryFieldExpression(
        "time", LogQueryOperator.LessThanOrEqual, endTime.ToString("O")));
    }

    if (parts.Count == 0)
      return LogQueryMatchAllExpression.Instance;

    return LogQueryExpression.AndAll(parts);
  }
}
