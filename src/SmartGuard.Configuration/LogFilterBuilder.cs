namespace SmartGuard.Configuration;

/// <summary>
/// 流式筛选构建器：链式调用构建 LogQueryFilterSpec。
/// </summary>
public sealed class LogFilterBuilder
{
  private string? _keyword;
  private string? _level;
  private string? _category;
  private string? _operationId;
  private string? _traceId;
  private int? _processId;
  private int? _threadId;
  private string? _eventName;
  private DateTimeOffset? _startTime;
  private DateTimeOffset? _endTime;
  private string? _textExpression;
  private int _page = 1;
  private int _pageSize = 50;

  public static LogFilterBuilder Create() => new();

  public LogFilterBuilder WithKeyword(string keyword) { _keyword = keyword; return this; }
  public LogFilterBuilder WithLevel(string level) { _level = level.ToUpperInvariant(); return this; }
  public LogFilterBuilder WithCategory(string category) { _category = category; return this; }
  public LogFilterBuilder WithOperationId(string operationId) { _operationId = operationId; return this; }
  public LogFilterBuilder WithTraceId(string traceId) { _traceId = traceId; return this; }
  public LogFilterBuilder WithProcessId(int processId) { _processId = processId; return this; }
  public LogFilterBuilder WithThreadId(int threadId) { _threadId = threadId; return this; }
  public LogFilterBuilder WithEventName(string eventName) { _eventName = eventName; return this; }
  public LogFilterBuilder WithTimeRange(DateTimeOffset start, DateTimeOffset end) { _startTime = start; _endTime = end; return this; }
  public LogFilterBuilder WithPagination(int page, int pageSize) { _page = page; _pageSize = pageSize; return this; }

  public LogQueryFilterSpec Build() => new(
    _keyword, _level, _category, _operationId, _traceId,
    _processId, _threadId, _eventName, _startTime, _endTime,
    _page, _pageSize, _textExpression);

  public LogFilterBuilder WithTextExpression(string textExpression)
  {
    _textExpression = textExpression;
    return this;
  }
}
