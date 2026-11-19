using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

public sealed partial class GuardianLoop
{
  private readonly string _rootPath;
  private readonly string _configPath;
  private readonly string _initMarkerPath;
  private readonly string _fallbackLogPath;
  private readonly IdleTracker _idleTracker = new();
  private readonly BrightnessService _brightness;
  private readonly WmiProbeProcess _nativeProbe;
  private readonly LogDeduplicationFilter _deduplication = new(threshold: 5);
  private readonly SemaphoreSlim _wakeSignal = new(0, int.MaxValue);
  private readonly StatusPublisher _publisher;
  private readonly GuardConfigRepository _configRepository;
  private readonly GuardianLoopIterationState _iterationState = new();
  private readonly GuardObservationTracker _guardObservations = new();
  private readonly GuardianIterationRunner _iterationRunner;
  private readonly GuardianFirstRunInitializer _firstRunInitializer;
  private readonly GuardianExceptionStormHandler _exceptionStormHandler;
  private readonly LogPipeline _logPipeline;
  private readonly LogConsumerRecovery _consumerRecovery;
  private readonly LogPipelineHealth _pipelineHealth = new();
  private readonly HeartbeatSchedule _heartbeatSchedule = new();
  private readonly PowerSourceTransitionTracker _powerSourceTransitions = new();
  private readonly PowerLifecycleStateMachine _powerLifecycle = new();
  private readonly LogPipelineOptions _logOptions = new();
  private readonly LogBackpressureController _backpressure = new();
  private readonly GuardianCycleCoordinator _cycleCoordinator;
  private readonly TimeProvider _observationTime;
  private readonly IReadOnlyList<string> _supplementalMaintenancePaths;
  private bool _powerCfgBrightnessSupported;

  public GuardianLoop(
    string rootPath,
    string configPath,
    string statusPath,
    string initMarkerPath,
    string fallbackLogPath)
    : this(rootPath, configPath, statusPath, initMarkerPath, fallbackLogPath, true,
      supplementalMaintenancePaths: [fallbackLogPath, LogFallbackChain.EmergencyLogFile, CrashLogWriter.CrashLogFile, CrashRingBuffer.CrashRingLogFile])
  {
  }

  internal GuardianLoop(
    string rootPath,
    string configPath,
    string statusPath,
    string initMarkerPath,
    string fallbackLogPath,
    bool powerCfgBrightnessSupported,
    TimeProvider? timeProvider = null,
    IReadOnlyList<string>? supplementalMaintenancePaths = null)
  {
    _rootPath = rootPath;
    _observationTime = timeProvider ?? TimeProvider.System;
    // Isolated internal harnesses default to their own startup path, so they
    // never maintain the signed-in user's process-wide crash files.
    _supplementalMaintenancePaths = supplementalMaintenancePaths ?? [fallbackLogPath];
    _configPath = configPath;
    _initMarkerPath = initMarkerPath;
    _fallbackLogPath = fallbackLogPath;
    _lastLogSettings = new(LogLevel.Warn, null, null, _fallbackLogPath);
    _publisher = new StatusPublisher(statusPath, timeProvider);
    _configRepository = new GuardConfigRepository(configPath);
    _nativeProbe = new WmiProbeProcess(timeProvider: _observationTime, observe: ObserveNativeProbe);
    _brightness = new BrightnessService(_nativeProbe);
    _consumerRecovery = new LogConsumerRecovery(new LogRecoveryStateMachine(timeProvider: timeProvider));
    _powerCfgBrightnessSupported = powerCfgBrightnessSupported;
    _firstRunInitializer = new GuardianFirstRunInitializer(_initMarkerPath, _brightness, (c, l, m) => WriteLog(c, l, m, EventNameRegistry.AppStarted));
    _exceptionStormHandler = new GuardianExceptionStormHandler(_iterationState, (c, l, m) => WriteLog(c, l, m, EventNameRegistry.IterationError));
    _iterationRunner = new GuardianIterationRunner(
      _idleTracker,
      _brightness,
      _publisher,
      () => GuardConfigReadResult.TryFromRepository(_configRepository),
      (c, l, m, e, p, at) => WriteLog(c, l, m, e, properties: p, observedAtUtc: at),
      ObservePowerSource,
      () => _powerCfgBrightnessSupported,
      _heartbeatSchedule,
      // Shared observation state must never mix timestamps from different providers.
      timeProvider: _observationTime,
      readForeground: ReadForegroundForRunner,
      readForegroundEventsHealth: ReadForegroundEventsHealthForRunner,
      canMutatePolicy: () => _powerLifecycle.IsPolicyMutationAllowed,
      observationTracker: _guardObservations,
      nativeProbe: _nativeProbe,
      observeNativeProbe: ObserveNativeProbe);
    _cycleCoordinator = new GuardianCycleCoordinator(
      () => GuardConfigReadResult.TryFromRepository(_configRepository),
      ReadForegroundForRunner,
      _iterationState,
      _wakeSignal,
      (context, token) => _iterationRunner.RunAsync(_iterationState, context, token),
      timeProvider,
      consumePowerWake: ConsumePowerWake,
      observationTracker: _guardObservations);
    _foregroundReader = ForegroundReaderFactoryOverrideForTests?.Invoke() ?? new ForegroundProgramReader();
    _logPipeline = new LogPipeline(
      WriteConsumerBatch,
      _logOptions, timeProvider);
  }

  public void Run(CancellationToken cancellationToken = default)
    => RunAsync(cancellationToken).GetAwaiter().GetResult();

  // Single startup entry: probes the brightness capability the runner reads and performs the
  // one-time first-run alignment when the marker is absent.
  internal void RunStartupInitialization(GuardConfig config)
    => _firstRunInitializer.InitializeIfNeeded(config, ref _powerCfgBrightnessSupported);

  public async Task RunAsync(CancellationToken cancellationToken = default)
  {
    _logPipeline.Start();
    _logPipeline.StartHealthReporting(_pipelineHealth, TimeSpan.FromMinutes(5));
    try
    {
      EnsureConfigExists();
      var config = LoadConfig();
      WriteLog(config, LogLevel.Info, "SmartGuard Engine 启动", EventNameRegistry.AppStarted);
      // Before the guardian loop and before power notifications: no concurrent round exists yet.
      RunStartupInitialization(config);

      using var powerListener = PowerEventWakeListener.Create(HandlePowerEvent);
      var powerFailureReported = !powerListener.IsEnabled;
      if (!powerListener.IsEnabled)
        WriteLog(config, LogLevel.Warn, $"电源事件监听不可用：{powerListener.FailureReason}，将仅按轮询间隔工作", EventNameRegistry.AppStarted);

      while (!cancellationToken.IsCancellationRequested)
      {
        if (!powerListener.IsEnabled && !powerFailureReported)
        {
          powerFailureReported = true;
          WriteLog(config, LogLevel.Warn, $"电源事件监听已失效：{powerListener.FailureReason}", EventNameRegistry.AppStarted);
        }
        _deduplication.Reset();
        SyncForegroundSubscription();
        var directive = _powerLifecycle.TakeDirective();
        WriteLifecycleTransitions(config, directive.TransitionsToLog);
        if (directive.TransitionOverflowed)
          WriteLog(config, LogLevel.Warn, "电源生命周期转换队列已溢出，已丢弃最旧信号", EventNameRegistry.PowerLifecycleSignalOverflow);
        if (directive.RecoveryForced)
          WriteLog(config, LogLevel.Warn, "未在 30 秒内观察到恢复信号，进入受控恢复稳定期", EventNameRegistry.PowerLifecycleRecoveryForced);
        if (directive.ResetIdleEpoch)
          _heartbeatSchedule.ResetAfterResume();

        var intervalSeconds = ResolveWaitSeconds(directive.ReevaluateAfter);
        await RunGuardedCycleAsync(directive, intervalSeconds, cancellationToken);
        DrainGuardObservations();
        TryPublishLogHealth();

        WaitForWake(
          directive.RunIteration
            ? _cycleCoordinator.ComputeWaitDelay()
            : TimeSpan.FromSeconds(intervalSeconds),
          cancellationToken);
      }
    }
    finally
    {
      // 归属契约：监听归 GuardianLoop 启动/停止/等待退出——循环退出（含取消与异常路径）
      // 必须卸载 hook 并等待监听线程退出，不得留下回调写入协调器状态。
      StopForegroundListener();
      // AppStopping confirms intent only. Admission must precede freezing the
      // shutdown watermark; failure after that point stays in bounded memory.
      EnqueueLog(LogLevel.Info, EngineLogEvents.Create(LogLevel.Info,
        EventNameRegistry.AppStopping, "SmartGuard Engine 正在停止"));
      if (!await _logPipeline.StopAsync())
        CrashRingBuffer.Instance.Add(LogJsonlFormatter.FormatLine(EngineLogEvents.Create(
          LogLevel.Error, EventNameRegistry.IterationError, "日志关闭未确认完成，可能有记录未写入")));
      TryPublishLogHealth();
    }
  }

  private void ObservePowerSource(bool isOnAc)
  {
    if (!_powerSourceTransitions.Observe(isOnAc)) return;

    var cfg = LoadConfig();
    WriteLog(
      cfg,
      LogLevel.Info,
      PowerEventFormatter.FormatMessage(isOnAc),
      EventNameRegistry.PowerSourceChanged,
      LogEventOutcome.Handled);
  }

}
