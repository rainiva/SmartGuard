using System.Globalization;

namespace SmartGuard.Configuration;

/// <summary>
/// 日志查询服务：消费 LogIndex 按多条件过滤 + 分页返回。
/// 查询为纯内存操作，不读磁盘，支持 CancellationToken 取消。
/// </summary>
public sealed class LogQueryService
{
  public LogQueryResult Query(LogIndex index, LogQueryRequest request, CancellationToken cancellationToken)
  {
    var filtered = index.Entries.AsEnumerable();

    if (!string.IsNullOrEmpty(request.Level))
      filtered = filtered.Where(e => string.Equals(e.Level, request.Level, StringComparison.OrdinalIgnoreCase));

    if (!string.IsNullOrEmpty(request.Category))
      filtered = filtered.Where(e => string.Equals(e.Category, request.Category, StringComparison.OrdinalIgnoreCase));

    if (!string.IsNullOrEmpty(request.OperationId))
      filtered = filtered.Where(e => string.Equals(e.OperationId, request.OperationId, StringComparison.Ordinal));

    if (!string.IsNullOrEmpty(request.TraceId))
      filtered = filtered.Where(e => e.TraceId == request.TraceId);

    if (!string.IsNullOrEmpty(request.EventName))
      filtered = filtered.Where(e => string.Equals(e.EventName, request.EventName, StringComparison.OrdinalIgnoreCase));

    if (request.ProcessId.HasValue)
      filtered = filtered.Where(e => e.ProcessId == request.ProcessId.Value);

    if (request.ThreadId.HasValue)
      filtered = filtered.Where(e => e.ThreadId == request.ThreadId.Value);

    if (!string.IsNullOrEmpty(request.Keyword))
    {
      var comparison = StringComparison.OrdinalIgnoreCase;
      filtered = filtered.Where(e =>
        (e.EventName?.Contains(request.Keyword, comparison) == true) ||
        (e.OperationId?.Contains(request.Keyword, comparison) == true) ||
        (e.Category?.Contains(request.Keyword, comparison) == true));
    }

    var materialized = filtered.ToList();

    if (cancellationToken.IsCancellationRequested)
      return new LogQueryResult(Array.Empty<LogIndexEntry>(), 0, request.Page, request.PageSize);

    var totalCount = materialized.Count;
    var pageSize = Math.Max(1, request.PageSize);
    var page = Math.Max(1, request.Page);
    var skip = (page - 1) * pageSize;
    var pageItems = skip < totalCount
      ? materialized.Skip(skip).Take(pageSize).ToList()
      : new List<LogIndexEntry>();

    return new LogQueryResult(pageItems, totalCount, page, pageSize);
  }
}
