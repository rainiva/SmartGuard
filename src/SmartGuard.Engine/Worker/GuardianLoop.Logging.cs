using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

public sealed partial class GuardianLoop
{
  private void WriteLog(
    GuardConfig config,
    LogLevel level,
    string message,
    string eventName = EventNameRegistry.IterationError,
    string? outcome = null)
  {
    if (!LogLevelFilter.ShouldWrite(level, LogLevelFilter.ParseMinimum(config.LogMinimumLevel)))
      return;
    if (level == LogLevel.Heart
        && !_heartSampler.ShouldPersist(DateTime.Now, TimeSpan.FromSeconds(Math.Max(0, config.LogHeartMinIntervalSec))))
      return;
    var fp = message.Trim().ToLowerInvariant();
    if (!_deduplication.TryAccept(fp, message, out var dedupMessage))
      return;

    var logEvent = EngineLogEvents.Create(
      level,
      eventName,
      dedupMessage!,
      OperationIdContext.Current,
      TraceIdContext.Current,
      SpanIdContext.Current,
      UserActionIdContext.Current,
      outcome);
    var priority = level is LogLevel.Warn or LogLevel.Error or LogLevel.Critical ? LogPriority.High : LogPriority.Normal;
    if (priority == LogPriority.High)
      CrashRingBuffer.Instance.Add(LogJsonlFormatter.FormatLine(logEvent));
    if (priority == LogPriority.Normal && _backpressure.ShouldThrottle(_logPipeline.NormalQueueDepth, _logOptions.NormalQueueCapacity))
      return;
    if (_logPipeline.TryEnqueue(priority, logEvent))
      return;

    var logPath = SmartGuardPaths.ResolveLogFile(config, _rootPath);
    var retention = LogRetentionPolicy.FromGuardConfig(config);
    LogFallbackChain.WriteEvent(
      () => LogStructuredSink.TryWriteBatch(logPath, new[] { logEvent }, retention),
      _fallbackLogPath,
      logEvent);
  }

  private bool WriteBatchToDisk(IReadOnlyList<LogEvent> events)
  {
    try
    {
      var config = LoadConfig();
      var logPath = SmartGuardPaths.ResolveLogFile(config, _rootPath);
      var retention = LogRetentionPolicy.FromGuardConfig(config);
      if (LogStructuredSink.TryWriteBatch(logPath, events, retention))
        return true;

      foreach (var logEvent in events)
        LogFallbackChain.WriteEvent(() => false, _fallbackLogPath, logEvent);
      return true;
    }
    catch
    {
      return false;
    }
  }
}
