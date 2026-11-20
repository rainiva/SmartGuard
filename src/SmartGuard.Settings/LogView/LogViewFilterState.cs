using SmartGuard.Configuration;

namespace SmartGuard.Settings.LogView;

internal sealed record LogViewFilterState(
  LogQueryExpression QueryExpression,
  IReadOnlyList<string> ActiveTagFilters,
  bool HasActiveFilters)
{
  internal static LogViewFilterState FromLegacyFields(
    string searchKeyword,
    bool searchCaseSensitive,
    IReadOnlyList<string> activeTagFilters,
    LogViewTimeRange timeRange,
    DateTime? customRangeStart,
    DateTime? customRangeEnd,
    string operationIdFilter,
    string eventNameFilter,
    int? processIdFilter,
    int? threadIdFilter,
    Func<DateTime>? nowProvider,
    string componentFilter = "")
  {
    _ = searchCaseSensitive;
    var parts = new List<LogQueryExpression>();

    if (!string.IsNullOrWhiteSpace(searchKeyword))
    {
      if (LooksLikeQueryExpression(searchKeyword))
      {
        var parsed = LogQueryParser.Parse(searchKeyword);
        if (parsed.IsValid && parsed.Expression is not null)
          parts.Add(parsed.Expression);
      }
      else
      {
        parts.Add(new LogQueryOrExpression(
          new LogQueryFieldExpression("message", LogQueryOperator.Contains, searchKeyword),
          new LogQueryOrExpression(
            new LogQueryFieldExpression("eventname", LogQueryOperator.Contains, searchKeyword),
            new LogQueryFieldExpression("component", LogQueryOperator.Contains, searchKeyword))));
      }
    }

    if (timeRange != LogViewTimeRange.All)
    {
      var now = nowProvider?.Invoke() ?? DateTime.Now;
      var (start, end) = LogViewTimeRangeFilter.ResolveBounds(
        timeRange, now, customRangeStart, customRangeEnd);
      if (start.HasValue)
      {
        parts.Add(new LogQueryFieldExpression(
          "time",
          LogQueryOperator.GreaterThanOrEqual,
          new DateTimeOffset(start.Value).ToString("O")));
      }

      if (end.HasValue)
      {
        parts.Add(new LogQueryFieldExpression(
          "time",
          LogQueryOperator.LessThanOrEqual,
          new DateTimeOffset(end.Value).ToString("O")));
      }
    }

    if (!string.IsNullOrWhiteSpace(operationIdFilter))
    {
      parts.Add(new LogQueryOrExpression(
        new LogQueryFieldExpression("operationid", LogQueryOperator.Contains, operationIdFilter),
        new LogQueryFieldExpression("message", LogQueryOperator.Contains, "op=" + operationIdFilter)));
    }

    if (!string.IsNullOrWhiteSpace(eventNameFilter))
    {
      parts.Add(new LogQueryOrExpression(
        new LogQueryFieldExpression("eventname", LogQueryOperator.Contains, eventNameFilter),
        new LogQueryFieldExpression("message", LogQueryOperator.Contains, eventNameFilter)));
    }

    if (processIdFilter is int processId)
      parts.Add(new LogQueryFieldExpression("processid", LogQueryOperator.Equals, processId.ToString()));

    if (threadIdFilter is int threadId)
      parts.Add(new LogQueryFieldExpression("threadid", LogQueryOperator.Equals, threadId.ToString()));

    if (!string.IsNullOrWhiteSpace(componentFilter))
      parts.Add(new LogQueryFieldExpression("component", LogQueryOperator.Equals, componentFilter));

    var hasActiveFilters = parts.Count > 0 || activeTagFilters.Count > 0;
    var expression = parts.Count switch
    {
      0 => LogQueryMatchAllExpression.Instance,
      1 => parts[0],
      _ => LogQueryExpression.AndAll(parts),
    };

    return new LogViewFilterState(expression, activeTagFilters, hasActiveFilters);
  }

  internal bool Matches(LogEvent logEvent)
  {
    if (ActiveTagFilters.Count > 0)
    {
      var line = LogViewEventFilter.GetDisplayLine(logEvent);
      if (!LogLineTagParser.TryParse(line, out var tag, out _))
        return false;

      if (!MatchesTagFilter(tag, ActiveTagFilters))
        return false;
    }

    if (!HasActiveFilters || QueryExpression is LogQueryMatchAllExpression)
      return true;

    return LogQueryExpressionEvaluator.Matches(logEvent, QueryExpression);
  }

  internal LogViewFilterState WithScope(LogQueryExpression? scope)
  {
    if (scope is null or LogQueryMatchAllExpression)
      return this;

    var expression = QueryExpression is LogQueryMatchAllExpression
      ? scope
      : new LogQueryAndExpression(QueryExpression, scope);

    return this with
    {
      QueryExpression = expression,
      HasActiveFilters = true,
    };
  }

  private static bool MatchesTagFilter(string tag, IReadOnlyList<string> activeTags)
  {
    var active = activeTags.Select(v => v.ToUpperInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var normalized = tag.ToUpperInvariant();
    if (active.Contains(normalized))
      return true;

    return normalized == "\u76D1\u63A7\u4E2D" && active.Contains("HEART");
  }

  private static bool LooksLikeQueryExpression(string text)
    => text.Contains(':')
       || text.Contains('(')
       || text.Contains('"')
       || text.StartsWith('-')
       || text.Contains(" AND ", StringComparison.OrdinalIgnoreCase)
       || text.Contains(" OR ", StringComparison.OrdinalIgnoreCase)
       || text.StartsWith("NOT ", StringComparison.OrdinalIgnoreCase);
}
