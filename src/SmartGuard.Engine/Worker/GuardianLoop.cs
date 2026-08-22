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
      (c, l, m, e) => WriteLog(c, l, m, e),
      ObservePowerSource,
      _powerCfgBrightnessSupported,
      _heartbeatSchedule);
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
      if (!powerListener.IsEnabled)
        WriteLog(config, LogLevel.Warn, "电源事件监听不可用（SystemEvents），将仅按轮询间隔工作", EventNameRegistry.AppStarted);

      while (!cancellationToken.IsCancellationRequested)
      {
        _deduplication.Reset();
        var directive = _powerLifecycle.TakeDirective();
        WriteLifecycleTransitions(config, directive.TransitionsToLog);
        if (directive.TransitionOverflowed)
          WriteLog(config, LogLevel.Warn, "电源生命周期转换队列已溢出，已丢弃最旧信号", EventNameRegistry.PowerLifecycleSignalOverflow);
        if (directive.RecoveryForced)
          WriteLog(config, LogLevel.Warn, "未在 30 秒内观察到恢复信号，进入受控恢复稳定期", EventNameRegistry.PowerLifecycleRecoveryForced);
        if (directive.ResetIdleEpoch)
          _heartbeatSchedule.ResetAfterResume();
        if (!directive.RunIteration)
        {
          WaitForNextIteration(
            ResolveWaitSeconds(directive.ReevaluateAfter),
            cancellationToken);
          continue;
        }

        try
        {
          await _iterationRunner.RunAsync(
            _iterationState,
            new GuardianIterationContext(
              directive.AllowPolicyMutation,
              directive.ResetIdleEpoch,
              directive.ForcePowerRefresh,
              directive.AbsorbCurrentPlanAsBaseline,
              directive.Phase),
            cancellationToken);
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
            SmartGuardLogWriter.TryAppendLine(_fallbackLogPath, $"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex.Message}");
          }
        }

        WaitForNextIteration(ResolveWaitSeconds(directive.ReevaluateAfter), cancellationToken);
      }
    }
    finally
    {
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

  private void WaitForNextIteration(int intervalSeconds, CancellationToken cancellationToken)
  {
    if (_wakeSignal.Wait(0, cancellationToken)) return;
    _wakeSignal.Wait(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
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
