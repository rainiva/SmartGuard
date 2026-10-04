using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Engine.Infrastructure;
using System.Text.Json;

namespace SmartGuard.Engine.Worker;

public sealed partial class GuardianLoop
{
  private readonly record struct LogSettingsSnapshot(
    LogLevel MinimumLevel, string? MainPath, LogRetentionPolicy? MainRetention, string StartupPath);
  private readonly object _logSettingsGate = new();
  private LogSettingsSnapshot _lastLogSettings;

  private LogSettingsSnapshot CaptureLogSettings(GuardConfig? config)
  {
    if (config is not null)
    {
      // Prepare outside the short publication lock; this is path/value projection
      // only. The snapshot cannot carry an old policy or manual deadline.
      var next = new LogSettingsSnapshot(LogLevelFilter.ParseMinimum(config.LogMinimumLevel),
        SmartGuardPaths.ResolveLogFile(config, _rootPath), LogRetentionPolicy.FromGuardConfig(config), _fallbackLogPath);
      lock (_logSettingsGate) _lastLogSettings = next;
      return next;
    }
    lock (_logSettingsGate) return _lastLogSettings;
  }

  private GuardianLogWriteResult WriteLog(
    GuardConfig? config,
    LogLevel level,
    string message,
    string eventName = EventNameRegistry.IterationError,
    string? outcome = null,
    IReadOnlyDictionary<string, JsonElement>? properties = null,
    DateTimeOffset? observedAtUtc = null)
  {
    var settings = CaptureLogSettings(config);
    if (!LogLevelFilter.ShouldWrite(level, settings.MinimumLevel))
      return GuardianLogWriteResult.SuppressedByLevel;
    var dedupMessage = message;
    // These transitions already have semantic acknowledgement in the tracker.
    // Same-count rule replacement and different GUID facts may share text;
    // message-only aggregation must not suppress a real state change.
    if (eventName is not EventNameRegistry.ConfigReloaded and not EventNameRegistry.ForegroundAdmissionChanged and not EventNameRegistry.GuardStateChanged)
    {
      var fp = message.Trim().ToLowerInvariant();
      if (!_deduplication.TryAccept(fp, message, out dedupMessage)) return GuardianLogWriteResult.Rejected;
    }

    var logEvent = EngineLogEvents.Create(
      level,
      eventName,
      dedupMessage!,
      OperationIdContext.Current,
      TraceIdContext.Current,
      SpanIdContext.Current,
      UserActionIdContext.Current,
      outcome, properties);
    if (observedAtUtc is DateTimeOffset at) logEvent = logEvent with { Timestamp = at };
    return EnqueueLog(level, logEvent);
  }

  private void DrainGuardObservations()
  {
    var read = GuardConfigReadResult.TryFromRepository(_configRepository);
    _guardObservations.ObserveContext(read, GuardObservationGate.For(read, _observationTime.GetLocalNow()));
    _guardObservations.DrainConfiguration(read, _observationTime.GetUtcNow(),
      (c, l, m, e, p, at) => WriteLog(c, l, m, e, properties: p, observedAtUtc: at));
    _guardObservations.Drain(read.Config,
      (c, l, m, e, p, at) => WriteLog(c, l, m, e, properties: p, observedAtUtc: at));
  }

  private void WriteLifecycleTransitions(
    GuardConfig? config,
    IReadOnlyList<PowerLifecycleSignal> transitions)
  {
    foreach (var signal in transitions)
    {
      var (eventName, message) = signal.Kind switch
      {
        PowerLifecycleKind.Suspending =>
          (EventNameRegistry.PowerSuspendObserved, "电源生命周期：已观察到挂起信号"),
        PowerLifecycleKind.Resumed =>
          (EventNameRegistry.PowerResumeObserved, "电源生命周期：已观察到恢复信号"),
        _ => (EventNameRegistry.PowerSourceChanged, "电源生命周期：已观察到供电状态变化"),
      };

      var logEvent = EngineLogEvents.CreateLifecycle(
        LogLevel.Info,
        eventName,
        message,
        signal,
        outcome: LogEventOutcome.Handled);
      EnqueueLog(LogLevel.Info, logEvent);
    }
  }

  private GuardianLogWriteResult EnqueueLog(LogLevel level, LogEvent logEvent)
  {
    var priority = level is LogLevel.Warn or LogLevel.Error or LogLevel.Critical ? LogPriority.High : LogPriority.Normal;
    if (priority == LogPriority.High)
      CrashRingBuffer.Instance.Add(LogJsonlFormatter.FormatLine(logEvent));
    if (priority == LogPriority.Normal && _backpressure.ShouldThrottle(_logPipeline.NormalQueueDepth, _logOptions.NormalQueueCapacity))
    {
      _logPipeline.RecordBackpressureDrop();
      return GuardianLogWriteResult.DroppedByBackpressure;
    }
    if (_logPipeline.TryEnqueue(priority, logEvent))
      return GuardianLogWriteResult.AcceptedByPipeline;

    // The consumer owns every ordinary disk attempt. Full queues reject even
    // high-priority events; their bounded crash ring remains available.
    return GuardianLogWriteResult.Rejected;
  }

  private LogBatchWriteResult WriteConsumerBatch(IReadOnlyList<LogEvent> events)
  {
    var before = _consumerRecovery.State;
    var settings = CaptureLogSettings(LoadConfig());
    var result = _consumerRecovery.ExecuteWithRecovery(batch => WriteBatchToDisk(batch, settings), events, settings.StartupPath,
      primaryAvailable: settings.MainPath is not null && settings.MainRetention is not null);
    var after = _consumerRecovery.State;
    if ((before == RecoveryState.Healthy) != (after == RecoveryState.Healthy))
    {
      try
      {
        var restored = after == RecoveryState.Healthy;
        // Conversion events use bounded admission. An unavailable sink never
        // triggers recursive writes or a transition per fallback record.
        EnqueueLog(restored ? LogLevel.Info : LogLevel.Warn, EngineLogEvents.Create(
          restored ? LogLevel.Info : LogLevel.Warn,
          restored ? EventNameRegistry.RecoveryRestored : EventNameRegistry.RecoveryDegraded,
          restored ? "主日志写入已恢复" : "主日志写入降级，正在尝试回退",
          properties: new Dictionary<string, System.Text.Json.JsonElement>
          {
            ["fallbackWritten"] = System.Text.Json.JsonSerializer.SerializeToElement(result.FallbackWritten),
            ["lost"] = System.Text.Json.JsonSerializer.SerializeToElement(result.Lost),
          }));
      }
      catch
      {
        // Transition diagnostics cannot undo an already confirmed batch.
      }
    }
    return result;
  }

  private void TryPublishLogHealth()
  {
    try { _publisher.PublishLogHealth(_logPipeline.SampleLogHealth()); }
    catch
    {
      // State IO has the same local-failure boundary as ordinary status
      // publication. It cannot turn logging degradation into guard shutdown.
      if (_logPipeline.State == LogPipelineState.Running)
        EnqueueLog(LogLevel.Error, EngineLogEvents.Create(LogLevel.Error,
          EventNameRegistry.IterationError, "日志健康状态发布失败"));
    }
  }

  private static bool WriteBatchToDisk(IReadOnlyList<LogEvent> events, LogSettingsSnapshot settings)
  {
    try
    {
      return settings.MainPath is not null && settings.MainRetention is not null
        && LogStructuredSink.TryWriteBatch(settings.MainPath, events, settings.MainRetention);
    }
    catch
    {
      return false;
    }
  }
}
