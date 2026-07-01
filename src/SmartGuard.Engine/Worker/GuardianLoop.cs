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
  private readonly HashSet<string> _tickLogFingerprints = new(StringComparer.OrdinalIgnoreCase);
  private readonly SemaphoreSlim _wakeSignal = new(0, int.MaxValue);
  private readonly Dictionary<string, int> _exceptionCounts = new();
  private readonly StatusPublisher _publisher;
  private readonly GuardConfigRepository _configRepository;
  private readonly GuardianLoopIterationState _iterationState = new();
  private readonly GuardianIterationRunner _iterationRunner;
  private DateTime _exceptionWindowStart = DateTime.MinValue;
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
    _iterationRunner = new GuardianIterationRunner(
      _idleTracker,
      _brightness,
      _publisher,
      LoadConfig,
      WriteLog,
      _powerCfgBrightnessSupported);
  }

  public void Run(CancellationToken cancellationToken = default)
    => RunAsync(cancellationToken).GetAwaiter().GetResult();

  public async Task RunAsync(CancellationToken cancellationToken = default)
  {
    EnsureConfigExists();
    var config = LoadConfig();
    InitializeIfNeeded(config);
    WriteLog(config, LogLevel.Info, $"SmartGuard Engine 启动。日志：{config.LogFile}");

    using var powerListener = new PowerEventWakeListener(HandlePowerEvent);

    while (!cancellationToken.IsCancellationRequested)
    {
      _tickLogFingerprints.Clear();
      try
      {
        await _iterationRunner.RunAsync(_iterationState, cancellationToken);
      }
      catch (Exception ex)
      {
        try
        {
          var cfg = LoadConfig();
          TrackAndLogException(ex, cfg);
        }
        catch
        {
          FileLogger.Write(LogLevel.Error, _fallbackLogPath, ex.Message, long.MaxValue);
        }
      }

      _iterationState.LastKnownGuid = PowerCfgExecutor.GetCurrentPlanGuid();
      WaitForNextIteration(GuardianIterationTiming.ResolveWaitSeconds(_configRepository, _rootPath), cancellationToken);
    }
  }

  private void HandlePowerEvent(bool isOnAc)
  {
    try
    {
      var cfg = LoadConfig();
      WriteLog(cfg, LogLevel.Info, PowerEventFormatter.FormatMessage(isOnAc));
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

  private void InitializeIfNeeded(GuardConfig config)
  {
    if (File.Exists(_initMarkerPath)) return;
    WriteLog(config, LogLevel.Info, "INIT: 开始首次初始化...");
    _powerCfgBrightnessSupported = PowerCfgExecutor.IsBrightnessSupported(config.BalancedPlanGuid);
    if (_powerCfgBrightnessSupported)
    {
      foreach (var g in new[] { config.ActivePlanGuid, config.BalancedPlanGuid, config.PowerSaverPlanGuid })
        PowerCfgExecutor.DisableAdaptiveBrightness(g);

      var b = _brightness.GetBrightness();
      if (b >= 0)
      {
        foreach (var g in new[] { config.ActivePlanGuid, config.BalancedPlanGuid, config.PowerSaverPlanGuid })
          PowerCfgExecutor.SyncPlanBrightness(g, b);
        WriteLog(config, LogLevel.Info, $"INIT: powercfg 三计划亮度对齐为 {b}%");
      }
    }
    else
    {
      WriteLog(config, LogLevel.Info, "INIT: 本机不支持 powercfg 亮度项，已切换为 WMI-only 模式（切计划后写回亮度）");
    }

    WriteLog(config, LogLevel.Info, "INIT: 请手动关闭 CABC 与节电模式降亮度");
    File.WriteAllText(_initMarkerPath, DateTime.Now.ToString("s"));
    WriteLog(config, LogLevel.Info, "INIT: 首次初始化完成");
  }

  private GuardConfig LoadConfig() => _configRepository.LoadOrDefault(_rootPath);

  private void EnsureConfigExists()
  {
    if (!Directory.Exists(_rootPath)) Directory.CreateDirectory(_rootPath);
    if (File.Exists(_configPath)) return;
    _configRepository.Save(GuardConfig.CreateDefault(_rootPath));
  }

  private void WriteLog(GuardConfig config, LogLevel level, string message)
  {
    var fp = message.Trim().ToLowerInvariant();
    if (!_tickLogFingerprints.Add(fp)) return;
    try
    {
      FileLogger.Write(level, config.LogFile, message, config.LogMaxBytes);
    }
    catch
    {
      FileLogger.Write(LogLevel.Warn, _fallbackLogPath, $"[LOG-FALLBACK] {message}", long.MaxValue);
    }
  }

  private void TrackAndLogException(Exception ex, GuardConfig cfg)
  {
    const int windowSeconds = 120;
    const int threshold = 5;
    var key = ex.GetType().Name;
    var now = DateTime.UtcNow;
    if (now - _exceptionWindowStart > TimeSpan.FromSeconds(windowSeconds))
    {
      _exceptionWindowStart = now;
      _exceptionCounts.Clear();
    }

    _exceptionCounts[key] = _exceptionCounts.GetValueOrDefault(key) + 1;
    var count = _exceptionCounts[key];
    var suffix = count >= threshold
      ? $" (同类异常 {count} 次，将重新初始化状态以恢复)"
      : string.Empty;
    WriteLog(cfg, LogLevel.Error, ex.Message + suffix);
    if (count >= threshold)
    {
      _iterationState.LastKnownGuid = null;
      _iterationState.LastBrightness = null;
      _iterationState.ScriptJustSwitched = false;
      _exceptionWindowStart = DateTime.MinValue;
      _exceptionCounts.Clear();
    }
  }
}
