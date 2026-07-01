using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

internal sealed class GuardianLoopIterationState
{
  public IReadOnlyDictionary<Guid, string>? PlanCatalog;
  public Guid? LastKnownGuid;
  public string LastStatusLabel = string.Empty;
  public DateTime LastHeartbeat = DateTime.MinValue;
  public bool ScriptJustSwitched;
  public int? LastBrightness;
  public NotificationEventRetentionState NotificationRetention;
}

internal sealed class GuardianIterationRunner(
  IdleTracker idleTracker,
  BrightnessService brightness,
  StatusPublisher publisher,
  Func<GuardConfig> loadConfig,
  Action<GuardConfig, LogLevel, string> writeLog,
  bool powerCfgBrightnessSupported)
{
  public async Task RunAsync(GuardianLoopIterationState state, CancellationToken cancellationToken)
  {
    var config = loadConfig();
    var planCatalog = state.PlanCatalog ?? PowerPlanCatalogProvider.TryLoad();
    state.PlanCatalog = planCatalog;
    var idle = (int)idleTracker.Sample(IdleDetector.GetIdleSeconds, DateTime.UtcNow);
    var (batteryPercent, isOnAc) = BatteryInfoProvider.GetBatteryInfo();
    var activePlanInfo = PowerCfgExecutor.GetCurrentPlanInfo();
    var current = activePlanInfo?.Guid;
    var expected = PolicyEngine.GetExpectedPlanGuid(idle, isOnAc, batteryPercent, config);
    NotificationEvent? notifyEvent = null;

    if (PolicyEngine.IsExternalPlanChange(state.LastKnownGuid, current, state.ScriptJustSwitched))
    {
      var name = ResolvePlanName(current, config, planCatalog, activePlanInfo);
      writeLog(config, LogLevel.Warn, $"EXTERNAL: 计划被外部改为 {name} ({current}) | 下轮纠偏");
      notifyEvent = CreateExternalNotification(name);
    }
    state.ScriptJustSwitched = false;

    var label = PolicyEngine.GetStatusLabel(idle, config);
    var bright = brightness.GetBrightness();
    LogBrightnessChangeIfNeeded(config, bright, state, writeLog);

    if (expected is not null && PolicyEngine.ShouldApplyPowerPlanSwitch(current, expected))
    {
      var result = await SwitchWithBrightnessLockAsync(expected.Value, bright, config, cancellationToken);
      state.ScriptJustSwitched = true;
      activePlanInfo = PowerCfgExecutor.GetCurrentPlanInfo();
      current = activePlanInfo?.Guid;
      var pwr = isOnAc ? "插电" : "电池";
      var planName = ResolvePlanName(expected, config, planCatalog, activePlanInfo);
      if (bright >= 0)
      {
        writeLog(config, LogLevel.Info, $"状态: {label} | 计划切换(切前同步) + 亮度锁定: {result.Before}% -> {result.After}% | {planName} | 电量{batteryPercent}% {pwr}");
        if (result.After != result.Before)
          writeLog(config, LogLevel.Warn, $"亮度写回未完全匹配，已重试 {config.BrightnessRetryCount} 次");
      }
      else
      {
        writeLog(config, LogLevel.Info, $"状态: {label} | 计划切换(切前同步) | {planName} | 亮度WMI不支持");
      }
      state.LastStatusLabel = label;
      notifyEvent = CreatePlanSwitchNotification(planName, result.After, result.Before);
    }
    else if (state.LastStatusLabel != label)
    {
      var planName = ResolvePlanName(current, config, planCatalog, activePlanInfo);
      writeLog(config, LogLevel.Info,
        GuardianLogMessages.FormatStatusLabelChange(label, idle, planName, batteryPercent, isOnAc, bright));
      state.LastStatusLabel = label;
    }

    if (config.HeartbeatIntervalMin > 0 &&
        (DateTime.Now - state.LastHeartbeat).TotalMinutes >= config.HeartbeatIntervalMin)
    {
      var planName = ResolvePlanName(current, config, planCatalog, activePlanInfo);
      writeLog(config, LogLevel.Heart,
        GuardianLogMessages.FormatHeartbeat(label, planName, idle, batteryPercent, isOnAc, config.Paused, bright));
      state.LastHeartbeat = DateTime.Now;
    }

    state.NotificationRetention = NotificationEventRetention.Advance(
      notifyEvent,
      state.NotificationRetention,
      DateTime.Now);

    publisher.Publish(new StatusPayload
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
      enginePid = Environment.ProcessId,
      lastExternalChange = null,
      notificationEvent = state.NotificationRetention.Event
    });
  }

  private void LogBrightnessChangeIfNeeded(
    GuardConfig config,
    int brightness,
    GuardianLoopIterationState state,
    Action<GuardConfig, LogLevel, string> writeLog)
  {
    if (brightness < 0) return;

    if (state.LastBrightness is int previous && previous != brightness)
      writeLog(config, LogLevel.Info, GuardianLogMessages.FormatBrightnessChange(previous, brightness));

    state.LastBrightness = brightness;
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

  private async Task<(int Before, int After)> SwitchWithBrightnessLockAsync(
    Guid target,
    int brightnessBefore,
    GuardConfig config,
    CancellationToken cancellationToken)
  {
    if (powerCfgBrightnessSupported && brightnessBefore >= 0)
      PowerCfgExecutor.SyncPlanBrightness(target, brightnessBefore);

    PowerCfgExecutor.SetActivePlan(target);
    await Task.Delay(config.BrightnessRestoreMs, cancellationToken);

    if (brightnessBefore < 0) return (brightnessBefore, brightnessBefore);

    var after = await brightness.RestoreWithRetryAsync(
      brightnessBefore,
      config.BrightnessRetryCount,
      config.BrightnessRetryDelayMs,
      cancellationToken);
    return (brightnessBefore, after);
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
    => new()
    {
      kind = NotificationKinds.ExternalChange,
      title = "检测到外部计划变更",
      body = $"计划被外部改为 [{planName}]，守护将在下轮轮询时纠偏"
    };
}
