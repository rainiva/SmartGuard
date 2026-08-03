namespace SmartGuard.Configuration;

/// <summary>
/// 日志查询请求：多条件筛选 + 分页参数。
/// 所有筛选字段为可选（null 表示不限制）。
/// </summary>
public sealed record LogQueryRequest(
  string? Keyword,
  string? Level,
  string? Category,
  string? OperationId,
  string? TraceId,
  int? ProcessId,
  int? ThreadId,
  string? EventName,
  int Page,
  int PageSize,
  string? TextExpression = null)
{
  public static LogQueryRequest FromFields(
    string? keyword,
    string? level,
    string? category,
    string? operationId,
    string? traceId,
    int? processId,
    int? threadId,
    string? eventName,
    DateTimeOffset? startTime = null,
    DateTimeOffset? endTime = null,
    int page = 1,
    int pageSize = 50,
    string? textExpression = null)
    => new(keyword, level, category, operationId, traceId, processId, threadId, eventName, page, pageSize, textExpression);

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

    if (parts.Count == 0)
      return LogQueryMatchAllExpression.Instance;

    return LogQueryExpression.AndAll(parts);
  }
}
