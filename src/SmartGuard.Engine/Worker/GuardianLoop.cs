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
  private readonly LogPipelineHealth _pipelineHealth = new();
  private readonly HeartbeatSchedule _heartbeatSchedule = new();
  private readonly PowerSourceTransitionTracker _powerSourceTransitions = new();
  private readonly PowerLifecycleStateMachine _powerLifecycle = new();
  private readonly LogPipelineOptions _logOptions = new();
  private readonly LogBackpressureController _backpressure = new();
  private readonly GuardianCycleCoordinator _cycleCoordinator;
  private bool _powerCfgBrightnessSupported;

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
    bool powerCfgBrightnessSupported,
    TimeProvider? timeProvider = null)
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
      () => GuardConfigReadResult.TryFromRepository(_configRepository),
      (c, l, m, e) => WriteLog(c, l, m, e),
      ObservePowerSource,
      _powerCfgBrightnessSupported,
      _heartbeatSchedule,
      readForeground: ReadForegroundForRunner,
      readForegroundEventsHealth: ReadForegroundEventsHealthForRunner);
    _cycleCoordinator = new GuardianCycleCoordinator(
      () => GuardConfigReadResult.TryFromRepository(_configRepository),
      ReadForegroundForRunner,
      _iterationState,
      _wakeSignal,
      (context, token) => _iterationRunner.RunAsync(_iterationState, context, token),
      timeProvider,
      consumePowerWake: ConsumePowerWake);
    _foregroundReader = ForegroundReaderFactoryOverrideForTests?.Invoke() ?? new ForegroundProgramReader();
    _logPipeline = new LogPipeline(
      batch => _consumerRecovery.ExecuteWithRecovery(WriteBatchToDisk, batch, _fallbackLogPath),
      _logOptions);
  }

  public void Run(CancellationToken cancellationToken = default)
    => RunAsync(cancellationToken).GetAwaiter().GetResult();

  public async Task RunAsync(CancellationToken cancellationToken = default)
  {
    _logPipeline.Start();
    _logPipeline.StartHealthReporting(_pipelineHealth, TimeSpan.FromMinutes(5));
    try
    {
      EnsureConfigExists();
      var config = LoadConfig();
      var logPath = SmartGuardPaths.ResolveLogFile(config, _rootPath);
      WriteLog(config, LogLevel.Info, $"SmartGuard Engine 启动。日志：{logPath}", EventNameRegistry.AppStarted);

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
      await _logPipeline.StopAsync();
    }
  }

  private void HandlePowerEvent(PowerLifecycleSignal signal)
  {
    try
    {
      _powerLifecycle.Observe(signal);
    }
    catch
    {
      // State observation is memory-only; event-thread failures must not escape.
    }

    Interlocked.Exchange(ref _powerWakePending, 1);
    _wakeSignal.Release();
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

  private void WaitForWake(TimeSpan delay, CancellationToken cancellationToken)
  {
    if (delay < TimeSpan.Zero)
      delay = TimeSpan.Zero;
    if (!_wakeSignal.Wait(delay, cancellationToken))
      return;

    // 合并突发唤醒；唤醒原因（电源 / 前台 / 周期截止）由各自标记持有，不会因排空丢失。
    while (_wakeSignal.Wait(0, cancellationToken)) { }
  }

  private int ResolveWaitSeconds(TimeSpan? lifecycleReevaluateAfter)
    => GuardianIterationTiming.ResolveWaitSeconds(
      _configRepository,
      _rootPath,
      lifecycleReevaluateAfter);

  private GuardConfig LoadConfig() => _configRepository.LoadOrDefault(_rootPath);

  private void EnsureConfigExists()
  {
    if (!Directory.Exists(_rootPath)) Directory.CreateDirectory(_rootPath);
    if (File.Exists(_configPath)) return;
    _configRepository.Save(GuardConfig.CreateDefault(_rootPath));
  }
}
