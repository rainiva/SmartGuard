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
  public NotificationEvent? LastEmittedNotification;
  public DateTime LastEmittedNotificationAt = DateTime.MinValue;
}

internal sealed class GuardianIterationRunner(
  IdleTracker idleTracker,
  BrightnessService brightness,
  StatusPublisher publisher,
  Func<GuardConfig> loadConfig,
  Action<GuardConfig, LogLevel, string, string> writeLog,
  Action<bool> observePowerSource,
  bool powerCfgBrightnessSupported,
  Func<uint>? readIdleSeconds = null,
  Func<bool, (int Percent, bool IsOnAc)>? readBatteryInfo = null,
  Func<PowerPlanProbeResult>? readCurrentPlan = null,
  Func<Guid, CancellationToken, Task<PowerPlanApplyResult>>? applyPlan = null,
  Action<Guid, int>? syncPlanBrightness = null)
{
  public async Task RunAsync(
    GuardianLoopIterationState state,
    GuardianIterationContext context,
    CancellationToken cancellationToken)
  {
    OperationIdContext.GenerateNew();
    TraceIdContext.GenerateNew();
    var config = loadConfig();
    var planCatalog = state.PlanCatalog ?? PowerPlanCatalogProvider.TryLoad();
    state.PlanCatalog = planCatalog;
    if (context.ResetIdleEpoch)
      idleTracker.ResetAfterResume();
    var idle = (int)idleTracker.Sample(readIdleSeconds ?? IdleDetector.GetIdleSeconds);
    var (batteryPercent, isOnAc) = (readBatteryInfo ?? BatteryInfoProvider.GetBatteryInfo)(context.ForcePowerRefresh);
    observePowerSource(isOnAc);
    var planProbe = (readCurrentPlan ?? PowerCfgExecutor.ProbeCurrentPlan)();
    if (!planProbe.Succeeded)
    {
      writeLog(
        config,
        LogLevel.Warn,
        $"电源计划探测失败，保留上次计划状态：{planProbe.FailureKind} {planProbe.Diagnostic}",
        EventNameRegistry.PowerPlanProbeFailed);
      return;
    }

    var activePlanInfo = planProbe.Plan;
    var current = activePlanInfo?.Guid;
    if (context.AbsorbCurrentPlanAsBaseline)
    {
      state.LastKnownGuid = current;
      state.ScriptJustSwitched = false;
    }
    var expected = PolicyEngine.GetExpectedPlanGuid(idle, isOnAc, batteryPercent, config);
    var externalChangeDetected = context.AllowPolicyMutation &&
      !context.AbsorbCurrentPlanAsBaseline &&
      PolicyEngine.IsExternalPlanChange(
        state.LastKnownGuid, current, state.ScriptJustSwitched, expected);
    string? externalPlanNameForNotify = null;

    if (externalChangeDetected)
    {
      externalPlanNameForNotify = ResolvePlanName(current, config, planCatalog, activePlanInfo);
      writeLog(config, LogLevel.Warn, $"EXTERNAL: 计划被外部改为 {externalPlanNameForNotify} ({current}) | 下轮纠偏", EventNameRegistry.ExternalPlanChange);
    }
    state.ScriptJustSwitched = false;

    var label = PolicyEngine.GetStatusLabel(idle, config);
    var bright = brightness.GetBrightness();
    LogBrightnessChangeIfNeeded(config, bright, state, writeLog);

    var planSwitched = false;
    string? switchedPlanName = null;
    var switchBrightnessAfter = bright;
    var switchBrightnessBefore = bright;

    if (context.AllowPolicyMutation &&
        expected is not null &&
        PolicyEngine.ShouldApplyPowerPlanSwitch(current, expected))
    {
      var result = await SwitchWithBrightnessLockAsync(expected.Value, bright, config, cancellationToken);
      switchBrightnessBefore = result.Before;
      switchBrightnessAfter = result.After;
      if (!result.Apply.Succeeded)
      {
        writeLog(
          config,
          LogLevel.Warn,
          $"电源计划切换失败：{result.Apply.FailureKind} {result.Apply.Diagnostic}",
          EventNameRegistry.PowerPlanApplyFailed);
      }
      else
      {
        state.ScriptJustSwitched = true;
        planSwitched = true;
        if (result.Apply.ObservedGuid is Guid observedGuid)
        {
          current = observedGuid;
          activePlanInfo = new PowerSchemeInfo(observedGuid, null);
        }

        var pwr = isOnAc ? "插电" : "电池";
        switchedPlanName = ResolvePlanName(expected, config, planCatalog, activePlanInfo);
        if (bright >= 0)
        {
          writeLog(config, LogLevel.Info, $"状态: {label} | 计划切换(切前同步) + 亮度锁定: {result.Before}% -> {result.After}% | {switchedPlanName} | 电量{batteryPercent}% {pwr}", EventNameRegistry.PowerPlanSwitched);
          if (result.After != result.Before)
            writeLog(config, LogLevel.Warn, $"亮度写回未完全匹配，已重试 {config.BrightnessRetryCount} 次", EventNameRegistry.IterationError);
        }
        else
        {
          writeLog(config, LogLevel.Info, $"状态: {label} | 计划切换(切前同步) | {switchedPlanName} | 亮度WMI不支持", EventNameRegistry.PowerPlanSwitched);
        }
        state.LastStatusLabel = label;
      }
    }
    else if (state.LastStatusLabel != label)
    {
      var planName = ResolvePlanName(current, config, planCatalog, activePlanInfo);
      writeLog(config, LogLevel.Info,
        GuardianLogMessages.FormatStatusLabelChange(label, idle, planName, batteryPercent, isOnAc, bright),
        EventNameRegistry.IdleDetected);
      state.LastStatusLabel = label;
    }

    if (current is Guid observed && (!externalChangeDetected || planSwitched))
      state.LastKnownGuid = observed;

    if (config.HeartbeatIntervalMin > 0 &&
        (DateTime.Now - state.LastHeartbeat).TotalMinutes >= config.HeartbeatIntervalMin)
    {
      var planName = ResolvePlanName(current, config, planCatalog, activePlanInfo);
      writeLog(config, LogLevel.Heart,
        GuardianLogMessages.FormatHeartbeat(label, planName, idle, batteryPercent, isOnAc, config.Paused, bright),
        EventNameRegistry.HeartbeatTick);
      state.LastHeartbeat = DateTime.Now;
    }

    var now = DateTime.Now;
    var notifyEvent = NotificationEventComposer.Compose(
      externalChangeDetected,
      planSwitched,
      config.NotifyOnPlanChange,
      config.NotifyOnExternalChange,
      createPlanSwitch: () => CreatePlanSwitchNotification(
        switchedPlanName ?? string.Empty, switchBrightnessAfter, switchBrightnessBefore),
      createExternal: () => CreateExternalNotification(externalPlanNameForNotify ?? string.Empty));

    notifyEvent = NotificationEventComposer.ApplyCooldown(
      notifyEvent,
      state.LastEmittedNotification,
      state.LastEmittedNotificationAt,
      now);

    if (notifyEvent is not null)
    {
      state.LastEmittedNotification = notifyEvent;
      state.LastEmittedNotificationAt = now;
    }

    state.NotificationRetention = NotificationEventRetention.Advance(
      notifyEvent,
      state.NotificationRetention,
      now);

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
      operationId = OperationIdContext.Current,
      notificationEvent = state.NotificationRetention.Event
    });
  }

  private void LogBrightnessChangeIfNeeded(
    GuardConfig config,
    int brightness,
    GuardianLoopIterationState state,
    Action<GuardConfig, LogLevel, string, string> writeLog)
  {
    if (brightness < 0) return;

    if (state.LastBrightness is int previous && previous != brightness)
      writeLog(config, LogLevel.Info, GuardianLogMessages.FormatBrightnessChange(previous, brightness), EventNameRegistry.BrightnessAdjusted);

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

  private async Task<(PowerPlanApplyResult Apply, int Before, int After)> SwitchWithBrightnessLockAsync(
    Guid target,
    int brightnessBefore,
    GuardConfig config,
    CancellationToken cancellationToken)
  {
    if (powerCfgBrightnessSupported && brightnessBefore >= 0)
      (syncPlanBrightness ?? PowerCfgExecutor.SyncPlanBrightness)(target, brightnessBefore);

    Func<Guid, CancellationToken, Task<PowerPlanApplyResult>> applyOperation =
      applyPlan ?? ((targetGuid, token) => PowerCfgExecutor.ApplyAndVerifyAsync(
        targetGuid,
        PowerCfgExecutor.RunPowerCfgAsync,
        Task.Delay,
        token));
    var apply = await applyOperation(target, cancellationToken);
    if (!apply.Succeeded)
      return (apply, brightnessBefore, brightnessBefore);

    await Task.Delay(config.BrightnessRestoreMs, cancellationToken);

    if (brightnessBefore < 0) return (apply, brightnessBefore, brightnessBefore);

    var after = await brightness.RestoreWithRetryAsync(
      brightnessBefore,
      config.BrightnessRetryCount,
      config.BrightnessRetryDelayMs,
      cancellationToken);
    return (apply, brightnessBefore, after);
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
