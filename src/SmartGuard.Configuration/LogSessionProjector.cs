using System.Globalization;

namespace SmartGuard.Configuration;

public static class LogSessionProjector
{
  public static readonly TimeSpan LegacyGapThreshold = TimeSpan.FromMinutes(30);

  public static bool IsLifecycleStart(LogEvent logEvent) =>
    string.Equals(logEvent.EventName, EventNameRegistry.AppStarted, StringComparison.Ordinal);

  public static bool IsLifecycleStop(LogEvent logEvent) =>
    string.Equals(logEvent.EventName, EventNameRegistry.AppStopping, StringComparison.Ordinal);

  public static bool IsLegacyStartupSignal(LogEvent logEvent) =>
    IsLifecycleStart(logEvent) || ContainsStartupHint(logEvent.Message);

  public static IReadOnlyList<LogSessionMetadata> ProjectSessions(IEnumerable<LogEvent> events)
  {
    var builder = new LogSessionProjectionBuilder();
    foreach (var logEvent in events
               .OrderBy(item => item.Timestamp)
               .ThenBy(item => item.EventId, StringComparer.Ordinal))
    {
      builder.Add(logEvent);
    }

    return builder.Build();
  }

  internal static LogSessionProjectionBuilder CreateProjectionBuilder() => new();

  public static LogQueryExpression BuildSessionExpression(LogSessionMetadata session)
  {
    var startClause = new LogQueryFieldExpression(
      "time",
      LogQueryOperator.GreaterThanOrEqual,
      session.StartTimestamp.ToString("o", CultureInfo.InvariantCulture));

    if (session.IsActive || session.EndTimestamp is null)
      return startClause;

    var endClause = new LogQueryFieldExpression(
      "time",
      LogQueryOperator.LessThanOrEqual,
      session.EndTimestamp.Value.ToString("o", CultureInfo.InvariantCulture));

    return new LogQueryAndExpression(startClause, endClause);
  }

  public static LogQueryExpression CombineWithBase(LogQueryExpression? baseExpression, LogQueryExpression sessionClause)
  {
    if (baseExpression is null or LogQueryMatchAllExpression)
      return sessionClause;

    return new LogQueryAndExpression(baseExpression, sessionClause);
  }

  public static string BuildSessionTitle(LogSessionMetadata session)
  {
    var endText = session.EndTimestamp?.ToLocalTime().ToString("HH:mm:ss") ?? "进行中";
    var startText = session.StartTimestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    var activeSuffix = session.IsActive ? "（当前）" : string.Empty;
    return $"{startText} – {endText}{activeSuffix}";
  }

  public static bool EventMatchesSession(LogEvent logEvent, LogSessionMetadata session)
    => LogQueryExpressionEvaluator.Matches(logEvent, BuildSessionExpression(session));

  internal static bool ShouldStartLegacySession(LogEvent current, LogEvent previous)
  {
    if (IsLifecycleStart(current))
      return false;

    if (current.ProcessId != previous.ProcessId
        && current.ProcessId.HasValue
        && previous.ProcessId.HasValue)
      return true;

    if (IsLegacyStartupSignal(current) && !IsLifecycleStart(current))
      return true;

    return current.Timestamp - previous.Timestamp > LegacyGapThreshold;
  }

  private static bool ContainsStartupHint(string message) =>
    message.Contains("started", StringComparison.OrdinalIgnoreCase)
    || message.Contains("启动", StringComparison.OrdinalIgnoreCase);

  internal static LogSessionMetadata CreateMetadata(LogEvent startEvent, LogEvent endEvent, bool isActive)
    => new(
      SessionId: "s:" + startEvent.EventId,
      StartTimestamp: startEvent.Timestamp,
      EndTimestamp: endEvent.Timestamp,
      ProcessId: startEvent.ProcessId,
      Component: startEvent.Component,
      IsActive: isActive,
      StartEventId: startEvent.EventId,
      EndEventId: endEvent.EventId);
}

internal sealed class LogSessionProjectionBuilder
{
  private readonly List<LogSessionMetadata> _sessions = [];
  private LogEvent? _startEvent;
  private LogEvent? _lastEvent;
  private bool _betweenSessions;

  internal void Add(LogEvent current)
  {
    if (_startEvent is null)
    {
      _startEvent = current;
      _lastEvent = current;
      return;
    }

    var previous = _lastEvent ?? current;

    if (_betweenSessions)
    {
      if (LogSessionProjector.IsLifecycleStart(current)
          || LogSessionProjector.ShouldStartLegacySession(current, previous))
      {
        _startEvent = current;
        _betweenSessions = false;
        _lastEvent = current;
      }

      return;
    }

    if (LogSessionProjector.IsLifecycleStart(current))
    {
      _sessions.Add(LogSessionProjector.CreateMetadata(_startEvent, previous, isActive: false));
      _startEvent = current;
      _lastEvent = current;
      return;
    }

    if (LogSessionProjector.IsLifecycleStop(current))
    {
      _sessions.Add(LogSessionProjector.CreateMetadata(_startEvent, current, isActive: false));
      _betweenSessions = true;
      _lastEvent = current;
      return;
    }

    if (LogSessionProjector.ShouldStartLegacySession(current, previous))
    {
      _sessions.Add(LogSessionProjector.CreateMetadata(_startEvent, previous, isActive: false));
      _startEvent = current;
    }

    _lastEvent = current;
  }

  internal IReadOnlyList<LogSessionMetadata> Build()
  {
    if (_startEvent is null)
      return Array.Empty<LogSessionMetadata>();

    if (!_betweenSessions && _lastEvent is not null)
      _sessions.Add(LogSessionProjector.CreateMetadata(_startEvent, _lastEvent, isActive: true));

    return _sessions;
  }
}
