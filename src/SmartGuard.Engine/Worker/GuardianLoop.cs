using SmartGuard.Engine.Config;
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
  private readonly BrightnessService _brightness = new();
  private readonly HashSet<string> _tickLogFingerprints = new(StringComparer.OrdinalIgnoreCase);
  private Guid? _lastKnownGuid;
  private string _lastStatusLabel = string.Empty;
  private DateTime _lastHeartbeat = DateTime.MinValue;
  private bool _scriptJustSwitched;
  private bool _powerCfgBrightnessSupported = true;

  public void Run(CancellationToken cancellationToken = default)
  {
    EnsureConfigExists();
    var config = GuardConfig.LoadFromFile(configPath);
    InitializeIfNeeded(config);
    WriteLog(config, $"SmartGuard Engine 启动。日志：{config.LogFile}");

    while (!cancellationToken.IsCancellationRequested)
    {
      _tickLogFingerprints.Clear();
      try
      {
        config = GuardConfig.LoadFromFile(configPath);
        var idle = (int)IdleDetector.GetIdleSeconds();
        var (batteryPercent, isOnAc) = BatteryInfoProvider.GetBatteryInfo();
        var current = PowerCfgExecutor.GetCurrentPlanGuid();
        var expected = PolicyEngine.GetExpectedPlanGuid(idle, isOnAc, batteryPercent, config);
        NotificationEvent? notifyEvent = null;

        if (PolicyEngine.IsExternalPlanChange(_lastKnownGuid, current, _scriptJustSwitched))
        {
          var name = PolicyEngine.GetPlanDisplayName(current, config);
          WriteLog(config, $"EXTERNAL: 计划被外部改为 {name} ({current}) | 下轮纠偏");
          notifyEvent = CreateExternalNotification(name);
        }
        _scriptJustSwitched = false;

        var label = PolicyEngine.GetStatusLabel(idle, config);
        var bright = _brightness.GetBrightness();

        if (expected is not null && PolicyEngine.ShouldApplyPowerPlanSwitch(current, expected))
        {
          var result = SwitchWithBrightnessLock(expected.Value, bright, config);
          _scriptJustSwitched = true;
          current = PowerCfgExecutor.GetCurrentPlanGuid();
          var pwr = isOnAc ? "插电" : "电池";
          var planName = PolicyEngine.GetPlanDisplayName(expected, config);
          if (bright >= 0)
          {
            WriteLog(config, $"状态: {label} | 计划切换(切前同步) + 亮度锁定: {result.Before}% -> {result.After}% | {planName} | 电量{batteryPercent}% {pwr}");
            if (result.After != result.Before)
              WriteLog(config, $"WARN: 亮度写回未完全匹配，已重试 {config.BrightnessRetryCount} 次");
          }
          else
          {
            WriteLog(config, $"状态: {label} | 计划切换(切前同步) | {planName} | 亮度WMI不支持");
          }
          _lastStatusLabel = label;
          notifyEvent = CreatePlanSwitchNotification(planName, result.After, result.Before);
        }
        else if (_lastStatusLabel != label)
        {
          var pwr = isOnAc ? "插电" : "电池";
          var planName = PolicyEngine.GetPlanDisplayName(current, config);
          WriteLog(config, $"状态: {label} (空闲{idle}秒) | 计划正常 | {planName} | 电量{batteryPercent}% {pwr}");
          _lastStatusLabel = label;
        }

        if (config.HeartbeatIntervalMin > 0 &&
            (DateTime.Now - _lastHeartbeat).TotalMinutes >= config.HeartbeatIntervalMin)
        {
          var planName = PolicyEngine.GetPlanDisplayName(current, config);
          var pwr = isOnAc ? "插电" : "电池";
          var pause = config.Paused ? " | 已暂停" : string.Empty;
          WriteLog(config, $"[监控中] {label} | 计划正常 | {planName} | 电量{batteryPercent}% {pwr}{pause}");
          _lastHeartbeat = DateTime.Now;
        }

        new StatusPublisher(statusPath).Publish(new StatusPayload
        {
          timestamp = DateTime.Now.ToString("s"),
          currentPlan = PolicyEngine.GetPlanDisplayName(current, config),
          currentPlanGUID = current?.ToString(),
          expectedPlan = expected is null ? null : PolicyEngine.GetPlanDisplayName(expected, config),
          idleSeconds = idle,
          isOnAC = isOnAc,
          batteryPercent = batteryPercent,
          brightness = bright,
          paused = config.Paused,
          lastExternalChange = null,
          notificationEvent = notifyEvent
        });
      }
      catch (Exception ex)
      {
        try
        {
          var cfg = GuardConfig.LoadFromFile(configPath);
          WriteLog(cfg, $"ERROR: {ex.Message}");
        }
        catch
        {
          FileLogger.WriteLine(fallbackLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - ERROR: {ex.Message}");
        }
      }

      _lastKnownGuid = PowerCfgExecutor.GetCurrentPlanGuid();
      var interval = GuardConfig.LoadFromFile(configPath).CheckIntervalSec;
      Thread.Sleep(TimeSpan.FromSeconds(Math.Max(5, interval)));
    }
  }

  private (int Before, int After) SwitchWithBrightnessLock(Guid target, int brightnessBefore, GuardConfig config)
  {
    if (_powerCfgBrightnessSupported && brightnessBefore >= 0)
      PowerCfgExecutor.SyncPlanBrightness(target, brightnessBefore);

    PowerCfgExecutor.SetActivePlan(target);
    Thread.Sleep(config.BrightnessRestoreMs);

    if (brightnessBefore < 0) return (brightnessBefore, brightnessBefore);

    var after = _brightness.RestoreWithRetry(
      brightnessBefore,
      config.BrightnessRetryCount,
      config.BrightnessRetryDelayMs);
    return (brightnessBefore, after);
  }

  private void InitializeIfNeeded(GuardConfig config)
  {
    if (File.Exists(initMarkerPath)) return;
    WriteLog(config, "INIT: 开始首次初始化...");
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
        WriteLog(config, $"INIT: powercfg 三计划亮度对齐为 {b}%");
      }
    }
    else
    {
      WriteLog(config, "INIT: 本机不支持 powercfg 亮度项，已切换为 WMI-only 模式（切计划后写回亮度）");
    }
    WriteLog(config, "INIT: 请手动关闭 CABC 与节电模式降亮度");
    File.WriteAllText(initMarkerPath, DateTime.Now.ToString("s"));
    WriteLog(config, "INIT: 首次初始化完成");
  }

  private void EnsureConfigExists()
  {
    if (!Directory.Exists(rootPath)) Directory.CreateDirectory(rootPath);
    if (File.Exists(configPath)) return;
    var cfg = GuardConfig.CreateDefault(rootPath);
    File.WriteAllText(configPath, System.Text.Json.JsonSerializer.Serialize(cfg, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
  }

  private void WriteLog(GuardConfig config, string message)
  {
    var fp = message.Trim().ToLowerInvariant();
    if (!_tickLogFingerprints.Add(fp)) return;
    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
    try
    {
      FileLogger.RotateIfNeeded(config.LogFile, config.LogMaxBytes);
      FileLogger.WriteLine(config.LogFile, line);
    }
    catch
    {
      FileLogger.WriteLine(fallbackLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - [LOG-FALLBACK] {message}");
    }
  }

  private static NotificationEvent CreatePlanSwitchNotification(string planName, int brightness, int brightnessBefore)
  {
    var body = brightnessBefore >= 0 && brightnessBefore != brightness
      ? $"已切换至 [{planName}] 亮度 {brightnessBefore}% -> {brightness}%"
      : $"已切换至 [{planName}] (亮度 {brightness}%)";
    return new NotificationEvent
    {
      kind = "plan_switch",
      title = "电源计划已切换",
      body = body
    };
  }

  private static NotificationEvent CreateExternalNotification(string planName)
  {
    return new NotificationEvent
    {
      kind = "external_change",
      title = "检测到外部计划变更",
      body = $"计划被外部改为 [{planName}]，守护将在下轮轮询时纠偏"
    };
  }
}
