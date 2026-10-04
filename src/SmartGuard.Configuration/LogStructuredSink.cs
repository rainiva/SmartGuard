namespace SmartGuard.Configuration;

public static class LogStructuredSink
{
  public static bool TryWriteBatch(
    string logPath,
    IReadOnlyList<LogEvent> events,
    LogRetentionPolicy? retention,
    DateTime? now = null)
  {
    if (events.Count == 0)
      return true;

    var lines = events.Select(LogJsonlFormatter.FormatLine).ToArray();
    return SmartGuardLogWriter.TryAppendLines(logPath, lines, retention, now);
  }

  public static LogEvent CreateComponentEvent(
    LogSeverity severity,
    string component,
    string channel,
    string eventName,
    string message)
    => LogEventFactory.Create(
      severity,
      component,
      channel,
      eventName,
      message,
      processId: Environment.ProcessId,
      threadId: Environment.CurrentManagedThreadId);

  public static LogEvent CreatePipelineHealthEvent(LogHealthSnapshot snapshot)
  {
    var properties = new Dictionary<string, System.Text.Json.JsonElement>
    {
      ["enqueuedTotal"] = System.Text.Json.JsonSerializer.SerializeToElement(snapshot.EnqueuedTotal),
      ["droppedNormal"] = System.Text.Json.JsonSerializer.SerializeToElement(snapshot.DroppedNormal),
      ["droppedHigh"] = System.Text.Json.JsonSerializer.SerializeToElement(snapshot.DroppedHigh),
      ["writtenLinesTotal"] = System.Text.Json.JsonSerializer.SerializeToElement(snapshot.WrittenLinesTotal),
      ["failedBatches"] = System.Text.Json.JsonSerializer.SerializeToElement(snapshot.FailedBatches),
      ["primaryWrittenLines"] = System.Text.Json.JsonSerializer.SerializeToElement(snapshot.PrimaryWrittenLines),
      ["fallbackWrittenLines"] = System.Text.Json.JsonSerializer.SerializeToElement(snapshot.FallbackWrittenLines),
      ["lostLines"] = System.Text.Json.JsonSerializer.SerializeToElement(snapshot.LostLines),
      ["primaryFailedBatches"] = System.Text.Json.JsonSerializer.SerializeToElement(snapshot.PrimaryFailedBatches),
      ["droppedByBackpressure"] = System.Text.Json.JsonSerializer.SerializeToElement(snapshot.DroppedByBackpressure),
      ["consumerFailed"] = System.Text.Json.JsonSerializer.SerializeToElement(snapshot.ConsumerFailed),
      ["stopFailed"] = System.Text.Json.JsonSerializer.SerializeToElement(snapshot.StopFailed),
      ["normalQueueDepth"] = System.Text.Json.JsonSerializer.SerializeToElement(snapshot.NormalQueueDepth),
      ["highQueueDepth"] = System.Text.Json.JsonSerializer.SerializeToElement(snapshot.HighQueueDepth),
      ["enqueueRatePerSec"] = System.Text.Json.JsonSerializer.SerializeToElement(snapshot.EnqueueRatePerSec),
      ["writeRatePerSec"] = System.Text.Json.JsonSerializer.SerializeToElement(snapshot.WriteRatePerSec),
      ["averageLatencyMs"] = System.Text.Json.JsonSerializer.SerializeToElement(snapshot.AverageLatencyMs),
      ["p99LatencyMs"] = System.Text.Json.JsonSerializer.SerializeToElement(snapshot.P99LatencyMs),
    };

    return LogEventFactory.Create(
      LogSeverity.Info,
      LogComponentRegistry.Configuration,
      LogChannelRegistry.Diagnostics,
      EventNameRegistry.PipelineHealthSampled,
      "LogPipeline health sample",
      processId: Environment.ProcessId,
      properties: properties);
  }

  public static string FormatTextFallback(LogEvent logEvent)
  {
    var tag = LogSeverityParser.ToToken(logEvent.Severity);
    var local = logEvent.Timestamp.ToLocalTime();
    var suffix = BuildContextSuffix(
      logEvent.TraceId,
      logEvent.SpanId,
      logEvent.OperationId,
      logEvent.UserActionId);
    var baseLine = $"[{tag}] {local:yyyy-MM-dd HH:mm:ss} {logEvent.Message}";
    return suffix.Length == 0 ? baseLine : baseLine + " " + suffix;
  }

  private static string BuildContextSuffix(
    string? traceId,
    string? spanId,
    string? operationId,
    string? userActionId)
  {
    if (string.IsNullOrEmpty(traceId) && string.IsNullOrEmpty(spanId)
        && string.IsNullOrEmpty(operationId) && string.IsNullOrEmpty(userActionId))
      return string.Empty;

    var parts = new System.Text.StringBuilder();
    if (!string.IsNullOrEmpty(traceId)) parts.Append("trace=").Append(traceId).Append(' ');
    if (!string.IsNullOrEmpty(spanId)) parts.Append("span=").Append(spanId).Append(' ');
    if (!string.IsNullOrEmpty(operationId)) parts.Append("op=").Append(operationId).Append(' ');
    if (!string.IsNullOrEmpty(userActionId)) parts.Append("ua=").Append(userActionId).Append(' ');
    if (parts.Length == 0) return string.Empty;
    return parts.ToString(0, parts.Length - 1);
  }
}
