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
  public bool ScriptJustSwitched;

  /// <summary>
  /// 上一轮守护动作资格（自动决策是否 admitted）。有界运行状态，不是通用缓存：
  /// 从不干预恢复为 admitted 的那一轮先吸收当前计划为基线，避免把守护空窗期的
  /// 计划变化误报为刚发生的外部变更；null 表示尚无决策历史。
  /// </summary>
  public bool? LastForegroundAdmitted;

  public int? LastBrightness;
  public NotificationEventRetentionState NotificationRetention;
  public NotificationEvent? LastEmittedNotification;
  public DateTime LastEmittedNotificationAt = DateTime.MinValue;
}

internal sealed class GuardianIterationRunner(
  IdleTracker idleTracker,
  BrightnessService brightness,
  StatusPublisher publisher,
  Func<GuardConfigReadResult> loadConfig,
  Func<GuardConfig, LogLevel, string, string, GuardianLogWriteResult> writeLog,
  Action<bool> observePowerSource,
  bool powerCfgBrightnessSupported,
  HeartbeatSchedule heartbeatSchedule,
  Func<uint>? readIdleSeconds = null,
  Func<bool, (int Percent, bool IsOnAc)>? readBatteryInfo = null,
  Func<PowerPlanProbeResult>? readCurrentPlan = null,
  Func<Guid, CancellationToken, Task<PowerPlanApplyResult>>? applyPlan = null,
  Action<Guid, int>? syncPlanBrightness = null,
  TimeProvider? timeProvider = null,
  Func<ForegroundObservation>? readForeground = null)
{
  public async Task RunAsync(
    GuardianLoopIterationState state,
    GuardianIterationContext context,
    CancellationToken cancellationToken)
  {
    OperationIdContext.GenerateNew();
    TraceIdContext.GenerateNew();
    var time = timeProvider ?? TimeProvider.System;
    var nowUtc = time.GetUtcNow();
    var configRead = loadConfig();
    GuardConfig? configOpt = configRead.Config;
    var configAvailable = configRead.IsValid && configOpt is not null;
    var planCatalog = state.PlanCatalog ?? PowerPlanCatalogProvider.TryLoad();
    state.PlanCatalog = planCatalog;
    if (context.ResetIdleEpoch)
      idleTracker.ResetAfterResume();
    var idle = (int)idleTracker.Sample(readIdleSeconds ?? IdleDetector.GetIdleSeconds);
    var (batteryPercent, isOnAc) = (readBatteryInfo ?? BatteryInfoProvider.GetBatteryInfo)(context.ForcePowerRefresh);
    observePowerSource(isOnAc);
    var planProbe = (readCurrentPlan ?? PowerCfgExecutor.ProbeCurrentPlan)();
    var activePlanInfo = planProbe.Plan;
    var current = activePlanInfo?.Guid;

    if (!configAvailable)
    {
      // 配置不可用（缺失/破损/访问异常）：禁止新动作，也不从默认值重建人工期限；
      // 继续观察当前计划并发布观测负载，观察到的计划吸收为基线（非守护期间不产生积压纠偏）。
      AbsorbPlanAsBaseline(state, current);
      state.LastForegroundAdmitted = false;
      var brightnessDuringDegrade = brightness.GetBrightness();
      publisher.Publish(CreateDegradedObservationPayload(
        state, nowUtc, current, planCatalog, idle, isOnAc, batteryPercent, brightnessDuringDegrade));
      return;
    }

    if (!planProbe.Succeeded)
    {
      writeLog(
        configOpt!,
        LogLevel.Warn,
        $"电源计划探测失败，保留上次计划状态：{planProbe.FailureKind} {planProbe.Diagnostic}",
        EventNameRegistry.PowerPlanProbeFailed);
      return;
    }

    // 配置可用性已确认（configAvailable 为真），自此按有效配置执行全部决策与动作。
    var config = configOpt!;

    if (context.AbsorbCurrentPlanAsBaseline)
    {
      state.LastKnownGuid = current;
      state.ScriptJustSwitched = false;
    }

    var nowLocal = time.GetLocalNow().LocalDateTime;
    var foreground = readForeground?.Invoke() ?? ForegroundObservation.Unknown;
    var decision = PolicyEngine.Evaluate(idle, isOnAc, batteryPercent, foreground, config, nowLocal);
    var expected = decision.TargetPlanGuid;

    // 限制族不干预：守护资格挂起，观察到的计划持续吸收为基线，禁止发“将纠偏”通知。
    var guardingHeld = decision.Source is GuardDecisionSource.None
      && decision.Reason is GuardDecisionReason.ForegroundListEmpty
        or GuardDecisionReason.ForegroundIdentityUnknown
        or GuardDecisionReason.WaitingForListedProgram
        or GuardDecisionReason.ForegroundProgramPathsInvalid;

    if (guardingHeld)
    {
      AbsorbPlanAsBaseline(state, current);
      state.LastForegroundAdmitted = false;
    }
    else if (decision.Source is GuardDecisionSource.None)
    {
      // Paused：保持既有暂停行为（不吸收基线，外部变更判定照旧），仅推进准入跟踪。
      state.LastForegroundAdmitted = false;
    }
    else
    {
      var previouslyAdmitted = state.LastForegroundAdmitted;
      state.LastForegroundAdmitted = true;
      if (previouslyAdmitted == false)
      {
        // 从不干预进入守护：先吸收当前计划为基线，不补发守护空窗期的外部变更通知。
        AbsorbPlanAsBaseline(state, current);
      }
    }

    var externalChangeDetected = context.AllowPolicyMutation &&
      !context.AbsorbCurrentPlanAsBaseline &&
      !guardingHeld &&
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
        decision.Source is not GuardDecisionSource.None &&
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
        heartbeatSchedule.IsDue(
          TimeSpan.FromMinutes(config.HeartbeatIntervalMin),
          TimeSpan.FromSeconds(Math.Max(0, config.LogHeartMinIntervalSec))))
    {
      var planName = ResolvePlanName(current, config, planCatalog, activePlanInfo);
      var writeResult = writeLog(config, LogLevel.Heart,
        GuardianLogMessages.FormatHeartbeat(label, planName, idle, batteryPercent, isOnAc, config.Paused, bright),
        EventNameRegistry.HeartbeatTick);
      if (writeResult.IsAccepted())
        heartbeatSchedule.MarkAccepted();
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
      timestamp = nowUtc.ToString("O"),
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

  private static void AbsorbPlanAsBaseline(GuardianLoopIterationState state, Guid? current)
  {
    state.LastKnownGuid = current;
    state.ScriptJustSwitched = false;
  }

  /// <summary>
  /// 配置不可用时的降级观测负载：不携带决策目标（expectedPlan 为空），
  /// 不推断暂停态；仅发布尽力而为的当前计划探测结果与健康采集值。
  /// </summary>
  private StatusPayload CreateDegradedObservationPayload(
    GuardianLoopIterationState state,
    DateTimeOffset nowUtc,
    Guid? current,
    IReadOnlyDictionary<Guid, string>? planCatalog,
    int idle,
    bool isOnAc,
    int batteryPercent,
    int brightness)
    => new()
    {
      timestamp = nowUtc.ToString("O"),
      currentPlan = DescribePlanWithoutConfig(current, planCatalog),
      currentPlanGUID = current?.ToString(),
      expectedPlan = null,
      idleSeconds = idle,
      isOnAC = isOnAc,
      batteryPercent = batteryPercent,
      brightness = brightness,
      paused = false,
      enginePid = Environment.ProcessId,
      lastExternalChange = null,
      operationId = OperationIdContext.Current,
      notificationEvent = state.NotificationRetention.Event
    };

  private static string DescribePlanWithoutConfig(Guid? planGuid, IReadOnlyDictionary<Guid, string>? planCatalog)
  {
    if (planGuid is null) return string.Empty;
    if (planCatalog is not null
        && planCatalog.TryGetValue(planGuid.Value, out var name)
        && !string.IsNullOrWhiteSpace(name))
      return name;
    return planGuid.Value.ToString();
  }

  private void LogBrightnessChangeIfNeeded(
    GuardConfig config,
    int brightness,
    GuardianLoopIterationState state,
    Func<GuardConfig, LogLevel, string, string, GuardianLogWriteResult> writeLog)
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
