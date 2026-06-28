using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

public sealed class GuardianLoop(
  string rootPath,
  string configPath,
  string statusPath,
  string initMarkerPath,
  string fallbackLogPath)
{
  private readonly IdleTracker _idleTracker = new();
  private readonly BrightnessService _brightness = new();
  private readonly HashSet<string> _tickLogFingerprints = new(StringComparer.OrdinalIgnoreCase);
  private readonly SemaphoreSlim _wakeSignal = new(0, int.MaxValue);
  private readonly Dictionary<string, int> _exceptionCounts = new();
  private readonly StatusPublisher _publisher = new(statusPath);
  private IReadOnlyDictionary<Guid, string>? _planCatalog;
  private Guid? _lastKnownGuid;
  private DateTime _exceptionWindowStart = DateTime.MinValue;
  private string _lastStatusLabel = string.Empty;
  private DateTime _lastHeartbeat = DateTime.MinValue;
  private bool _scriptJustSwitched;
  private bool _powerCfgBrightnessSupported = true;
  private int? _lastBrightness;
  private NotificationEventRetentionState _notificationRetention;

  public void Run(CancellationToken cancellationToken = default)
  {
    RunAsync(cancellationToken).GetAwaiter().GetResult();
  }

  public async Task RunAsync(CancellationToken cancellationToken = default)
  {
    EnsureConfigExists();
    var config = GuardConfig.LoadFromFile(configPath);
    InitializeIfNeeded(config);
    WriteLog(config, LogLevel.Info, $"SmartGuard Engine 启动。日志：{config.LogFile}");

    using var powerListener = new PowerEventWakeListener(HandlePowerEvent);

    while (!cancellationToken.IsCancellationRequested)
    {
      _tickLogFingerprints.Clear();
      try
      {
        await ProcessIterationAsync(cancellationToken);
      }
      catch (Exception ex)
      {
        try
        {
          var cfg = GuardConfig.LoadFromFile(configPath);
          TrackAndLogException(ex, cfg);
        }
        catch
        {
          FileLogger.Write(LogLevel.Error, fallbackLogPath, ex.Message, long.MaxValue);
        }
      }

      _lastKnownGuid = PowerCfgExecutor.GetCurrentPlanGuid();
      WaitForNextIteration(GuardianIterationTiming.ResolveWaitSeconds(configPath), cancellationToken);
    }
  }

  private void HandlePowerEvent(bool isOnAc)
  {
    try
    {
      var cfg = GuardConfig.LoadFromFile(configPath);
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

  private async Task ProcessIterationAsync(CancellationToken cancellationToken)
  {
    var config = GuardConfig.LoadFromFile(configPath);
    var planCatalog = _planCatalog ?? PowerCfgExecutor.LoadPowerPlanCatalog();
    var idle = (int)_idleTracker.Sample(IdleDetector.GetIdleSeconds, DateTime.UtcNow);
    var (batteryPercent, isOnAc) = BatteryInfoProvider.GetBatteryInfo();
    var activePlanInfo = PowerCfgExecutor.GetCurrentPlanInfo();
    var current = activePlanInfo?.Guid;
    var expected = PolicyEngine.GetExpectedPlanGuid(idle, isOnAc, batteryPercent, config);
    NotificationEvent? notifyEvent = null;

    if (PolicyEngine.IsExternalPlanChange(_lastKnownGuid, current, _scriptJustSwitched))
    {
      var name = ResolvePlanName(current, config, planCatalog, activePlanInfo);
      WriteLog(config, LogLevel.Warn, $"EXTERNAL: 计划被外部改为 {name} ({current}) | 下轮纠偏");
      notifyEvent = CreateExternalNotification(name);
    }
    _scriptJustSwitched = false;

    var label = PolicyEngine.GetStatusLabel(idle, config);
    var bright = _brightness.GetBrightness();
    LogBrightnessChangeIfNeeded(config, bright);

    if (expected is not null && PolicyEngine.ShouldApplyPowerPlanSwitch(current, expected))
    {
      var result = await SwitchWithBrightnessLockAsync(expected.Value, bright, config, cancellationToken);
      _scriptJustSwitched = true;
      activePlanInfo = PowerCfgExecutor.GetCurrentPlanInfo();
      current = activePlanInfo?.Guid;
      var pwr = isOnAc ? "插电" : "电池";
      var planName = ResolvePlanName(expected, config, planCatalog, activePlanInfo);
      if (bright >= 0)
      {
        WriteLog(config, LogLevel.Info, $"状态: {label} | 计划切换(切前同步) + 亮度锁定: {result.Before}% -> {result.After}% | {planName} | 电量{batteryPercent}% {pwr}");
        if (result.After != result.Before)
          WriteLog(config, LogLevel.Warn, $"亮度写回未完全匹配，已重试 {config.BrightnessRetryCount} 次");
      }
      else
      {
        WriteLog(config, LogLevel.Info, $"状态: {label} | 计划切换(切前同步) | {planName} | 亮度WMI不支持");
      }
      _lastStatusLabel = label;
      notifyEvent = CreatePlanSwitchNotification(planName, result.After, result.Before);
    }
    else if (_lastStatusLabel != label)
    {
      var pwr = isOnAc ? "插电" : "电池";
      var planName = ResolvePlanName(current, config, planCatalog, activePlanInfo);
      WriteLog(config, LogLevel.Info,
        GuardianLogMessages.FormatStatusLabelChange(label, idle, planName, batteryPercent, isOnAc, bright));
      _lastStatusLabel = label;
    }

    if (config.HeartbeatIntervalMin > 0 &&
        (DateTime.Now - _lastHeartbeat).TotalMinutes >= config.HeartbeatIntervalMin)
    {
      var planName = ResolvePlanName(current, config, planCatalog, activePlanInfo);
      WriteLog(config, LogLevel.Heart,
        GuardianLogMessages.FormatHeartbeat(label, planName, idle, batteryPercent, isOnAc, config.Paused, bright));
      _lastHeartbeat = DateTime.Now;
    }

    _notificationRetention = NotificationEventRetention.Advance(
      notifyEvent,
      _notificationRetention,
      DateTime.Now);

    _publisher.Publish(new StatusPayload
    {
      timestamp = DateTime.Now.ToString("s"),
      currentPlan = ResolvePlanName(current, config, planCatalog, activePlanInfo),
      currentPlanGUID = current?.ToString(),
      expectedPlan = expected is null ? null : ResolvePlanName(expected, config, planCatalog, activePlanInfo),
      idleSeconds = idle,
      isOnAC = isOnAc,
      batteryPercent = batteryPercent,
      brightness = bright,
      paused = config.Paused,
      lastExternalChange = null,
      notificationEvent = _notificationRetention.Event
    });
  }

  private void LogBrightnessChangeIfNeeded(GuardConfig config, int brightness)
  {
    if (brightness < 0) return;

    if (_lastBrightness is int previous && previous != brightness)
      WriteLog(config, LogLevel.Info, GuardianLogMessages.FormatBrightnessChange(previous, brightness));

    _lastBrightness = brightness;
  }

  private static string ResolvePlanName(
    Guid? planGuid,
    GuardConfig config,
    IReadOnlyDictionary<Guid, string> planCatalog,
    PowerSchemeInfo? activePlanInfo)
  {
    var preferredName = planGuid == activePlanInfo?.Guid ? activePlanInfo?.Name : null;
    return PolicyEngine.GetPlanDisplayName(planGuid, config, planCatalog, preferredName);
  }

  private async Task<(int Before, int After)> SwitchWithBrightnessLockAsync(Guid target, int brightnessBefore, GuardConfig config, CancellationToken cancellationToken)
  {
    if (_powerCfgBrightnessSupported && brightnessBefore >= 0)
      PowerCfgExecutor.SyncPlanBrightness(target, brightnessBefore);

    PowerCfgExecutor.SetActivePlan(target);
    await Task.Delay(config.BrightnessRestoreMs, cancellationToken);

    if (brightnessBefore < 0) return (brightnessBefore, brightnessBefore);

    var after = await _brightness.RestoreWithRetryAsync(
      brightnessBefore,
      config.BrightnessRetryCount,
      config.BrightnessRetryDelayMs,
      cancellationToken);
    return (brightnessBefore, after);
  }

  private void InitializeIfNeeded(GuardConfig config)
  {
    if (File.Exists(initMarkerPath)) return;
    WriteLog(config, LogLevel.Info, "INIT: 开始首次初始化...");
    _powerCfgBrightnessSupported = PowerCfgExecutor.IsBrightnessSupported(config.BalancedPlanGuid);
    if (_powerCfgBrightnessSupported)
    {
      foreach (var g in new[] { config.ActivePlanGuid, config.BalancedPlanGuid, config.PowerSaverPlanGuid })
      {
        PowerCfgExecutor.DisableAdaptiveBrightness(g);
      }
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
    File.WriteAllText(initMarkerPath, DateTime.Now.ToString("s"));
    WriteLog(config, LogLevel.Info, "INIT: 首次初始化完成");
  }

  private void EnsureConfigExists()
  {
    if (!Directory.Exists(rootPath)) Directory.CreateDirectory(rootPath);
    if (File.Exists(configPath)) return;
    var cfg = GuardConfig.CreateDefault(rootPath);
    File.WriteAllText(configPath, System.Text.Json.JsonSerializer.Serialize(cfg, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
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
      FileLogger.Write(LogLevel.Warn, fallbackLogPath, $"[LOG-FALLBACK] {message}", long.MaxValue);
    }
  }

  private static NotificationEvent CreatePlanSwitchNotification(string planName, int brightness, int brightnessBefore)
  {
    var body = brightnessBefore >= 0 && brightnessBefore != brightness
      ? $"已切换至 [{planName}] 亮度 {brightnessBefore}% -> {brightness}%"
      : $"已切换至 [{planName}] (亮度 {brightness}%)";
    return new NotificationEvent
    {
      kind = NotificationKinds.PlanSwitch,
      title = "电源计划已切换",
      body = body
    };
  }

  private static NotificationEvent CreateExternalNotification(string planName)
  {
    return new NotificationEvent
    {
      kind = NotificationKinds.ExternalChange,
      title = "检测到外部计划变更",
      body = $"计划被外部改为 [{planName}]，守护将在下轮轮询时纠偏"
    };
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
      _lastKnownGuid = null;
      _lastBrightness = null;
      _scriptJustSwitched = false;
      _exceptionWindowStart = DateTime.MinValue;
      _exceptionCounts.Clear();
    }
  }
}
