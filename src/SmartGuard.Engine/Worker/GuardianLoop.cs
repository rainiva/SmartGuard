using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

public sealed class GuardianLoop
{
  private readonly string _rootPath;
  private readonly string _configPath;
  private readonly string _initMarkerPath;
  private readonly string _fallbackLogPath;
  private readonly IdleTracker _idleTracker = new();
  private readonly BrightnessService _brightness = new();
  private readonly LogDeduplicationFilter _deduplication = new(threshold: 5);
  private readonly SemaphoreSlim _wakeSignal = new(0, int.MaxValue);
  private readonly StatusPublisher _publisher;
  private readonly GuardConfigRepository _configRepository;
  private readonly GuardianLoopIterationState _iterationState = new();
  private readonly GuardianIterationRunner _iterationRunner;
  private readonly GuardianFirstRunInitializer _firstRunInitializer;
  private readonly GuardianExceptionStormHandler _exceptionStormHandler;
  private readonly LogPipeline _logPipeline;
  private readonly LogConsumerRecovery _consumerRecovery = new(new LogRecoveryStateMachine());
  private readonly HeartLogSampler _heartSampler = new();
  private readonly LogPipelineOptions _logOptions = new();
  private readonly LogBackpressureController _backpressure = new();
  private bool _powerCfgBrightnessSupported = true;

  public GuardianLoop(
    string rootPath,
    string configPath,
    string statusPath,
    string initMarkerPath,
    string fallbackLogPath)
    : this(rootPath, configPath, statusPath, initMarkerPath, fallbackLogPath, true)
  {
  }

  internal GuardianLoop(
    string rootPath,
    string configPath,
    string statusPath,
    string initMarkerPath,
    string fallbackLogPath,
    bool powerCfgBrightnessSupported)
  {
    _rootPath = rootPath;
    _configPath = configPath;
    _initMarkerPath = initMarkerPath;
    _fallbackLogPath = fallbackLogPath;
    _publisher = new StatusPublisher(statusPath);
    _configRepository = new GuardConfigRepository(configPath);
    _powerCfgBrightnessSupported = powerCfgBrightnessSupported;
    _firstRunInitializer = new GuardianFirstRunInitializer(_initMarkerPath, _brightness, (c, l, m) => WriteLog(c, l, m, EventNameRegistry.AppStarted));
    _exceptionStormHandler = new GuardianExceptionStormHandler(_iterationState, (c, l, m) => WriteLog(c, l, m, EventNameRegistry.IterationError));
    _iterationRunner = new GuardianIterationRunner(
      _idleTracker,
      _brightness,
      _publisher,
      LoadConfig,
      (c, l, m) => WriteLog(c, l, m, EventNameRegistry.HeartbeatTick),
      _powerCfgBrightnessSupported);
    _logPipeline = new LogPipeline(lines => _consumerRecovery.ExecuteWithRecovery(WriteBatchToDisk, lines, _fallbackLogPath), _logOptions); // 默认参数：200 条/100ms 先到先触发，关闭超时 3s
  }

  public void Run(CancellationToken cancellationToken = default)
    => RunAsync(cancellationToken).GetAwaiter().GetResult();

  public async Task RunAsync(CancellationToken cancellationToken = default)
  {
    _logPipeline.Start();
    try
    {
      EnsureConfigExists();
      var config = LoadConfig();
      _firstRunInitializer.InitializeIfNeeded(config, ref _powerCfgBrightnessSupported);
      var logPath = SmartGuardPaths.ResolveLogFile(config, _rootPath);
      WriteLog(config, LogLevel.Info, $"SmartGuard Engine 启动。日志：{logPath}", EventNameRegistry.AppStarted);

      using var powerListener = PowerEventWakeListener.Create(HandlePowerEvent);
      if (!powerListener.IsEnabled)
        WriteLog(config, LogLevel.Warn, "电源事件监听不可用（SystemEvents），将仅按轮询间隔工作", EventNameRegistry.AppStarted);

      while (!cancellationToken.IsCancellationRequested)
      {
        _deduplication.Reset();
        try
        {
          await _iterationRunner.RunAsync(_iterationState, cancellationToken);
        }
        catch (Exception ex)
        {
          try
          {
            var cfg = LoadConfig();
            _exceptionStormHandler.TrackAndLogException(ex, cfg);
          }
          catch
          {
            FileLogger.Write(LogLevel.Error, _fallbackLogPath, ex.Message, long.MaxValue);
          }
        }

        _iterationState.LastKnownGuid = GuardianPlanProbe.ProbeOrKeep(
          PowerCfgExecutor.GetCurrentPlanGuid,
          _iterationState.LastKnownGuid,
          message => WriteLog(config, LogLevel.Warn, message, EventNameRegistry.ExternalPlanChange));
        WaitForNextIteration(GuardianIterationTiming.ResolveWaitSeconds(_configRepository, _rootPath), cancellationToken);
      }
    }
    finally
    {
      // 受控关闭：停止接收 → 排空队列 → Flush → Stopped（3s 超时上限，超时也不阻塞退出）
      await _logPipeline.StopAsync();
    }
  }

  private void HandlePowerEvent(bool isOnAc)
  {
    try
    {
      var cfg = LoadConfig();
      WriteLog(cfg, LogLevel.Info, PowerEventFormatter.FormatMessage(isOnAc), EventNameRegistry.PowerPlanSwitched);
    }
    catch
    {
      // ignore logging failures on system event thread
    }

    _wakeSignal.Release();
  }

  private void WaitForNextIteration(int intervalSeconds, CancellationToken cancellationToken)
  {
    if (_wakeSignal.Wait(0, cancellationToken)) return;
    _wakeSignal.Wait(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
    while (_wakeSignal.Wait(0, cancellationToken)) { }
  }

  private GuardConfig LoadConfig() => _configRepository.LoadOrDefault(_rootPath);

  private void EnsureConfigExists()
  {
    if (!Directory.Exists(_rootPath)) Directory.CreateDirectory(_rootPath);
    if (File.Exists(_configPath)) return;
    _configRepository.Save(GuardConfig.CreateDefault(_rootPath));
  }

  private void WriteLog(GuardConfig config, LogLevel level, string message, string eventName = EventNameRegistry.IterationError)
  {
    if (!LogLevelFilter.ShouldWrite(level, LogLevelFilter.ParseMinimum(config.LogMinimumLevel)))
      return;
    if (level == LogLevel.Heart
        && !_heartSampler.ShouldPersist(DateTime.Now, TimeSpan.FromSeconds(Math.Max(0, config.LogHeartMinIntervalSec))))
      return;
    var fp = message.Trim().ToLowerInvariant();
    if (!_deduplication.TryAccept(fp, message, out var dedupMessage)) return;
    var logPath = SmartGuardPaths.ResolveLogFile(config, _rootPath);
    var line = LogLineFormatter.Format(DateTime.Now, level, dedupMessage!, TraceIdContext.Current, SpanIdContext.Current, OperationIdContext.Current, UserActionIdContext.Current);
    var priority = level is LogLevel.Warn or LogLevel.Error or LogLevel.Critical ? LogPriority.High : LogPriority.Normal;
    if (priority == LogPriority.High) CrashRingBuffer.Instance.Add(line); // 崩溃现场环形缓冲：高优先级行同步留存
    if (priority == LogPriority.Normal && _backpressure.ShouldThrottle(_logPipeline.NormalQueueDepth, _logOptions.NormalQueueCapacity)) return; // 背压：丢弃 Normal 低价值行
    if (_logPipeline.TryEnqueue(priority, line)) return;

    // 入队失败（队列满/管线已停）回退阶段一同步三层降级链，关键日志不丢
    LogFallbackChain.Write(
      () => FileLogger.TryWrite(level, logPath, dedupMessage!, config.LogMaxBytes),
      _fallbackLogPath,
      line);
  }

  private bool WriteBatchToDisk(IReadOnlyList<string> lines)
  {
    try
    {
      var config = LoadConfig();
      var logPath = SmartGuardPaths.ResolveLogFile(config, _rootPath);
      if (FileLogger.TryWriteLines(logPath, lines, config.LogMaxBytes))
      {
        FileLogger.EnforceArchiveLimits(logPath, config.LogMaxArchiveFiles, config.LogDirectoryMaxBytes);
        return true;
      }

      foreach (var line in lines)
        LogFallbackChain.Write(() => false, _fallbackLogPath, line); // 主层已失败，逐行走 startup/emergency 降级
      return true; // 已降级处置完毕，不让管线重复计失败批次
    }
    catch
    {
      return false; // 未预期异常：计入 FailedBatches，绝不上抛到消费线程
    }
  }
}
