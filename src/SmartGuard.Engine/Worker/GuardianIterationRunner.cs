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
  Func<ForegroundObservation>? readForeground = null,
  Func<bool?>? readForegroundEventsHealth = null,
  Func<int>? readBrightness = null,
  Func<bool>? canMutatePolicy = null)
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
    bool PolicyMutationAllowed() => context.AllowPolicyMutation && (canMutatePolicy?.Invoke() ?? true);
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
      var brightnessDuringDegrade = (readBrightness ?? brightness.GetBrightness)();
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
      // 探测失败同样必须发布状态：否则 UI 停留在上一轮原因，无法解释守护暂停纠偏。
      publisher.Publish(CreateProbeFailedObservationPayload(
        state, configOpt!, nowUtc, current, planCatalog, idle, isOnAc, batteryPercent, bright: (readBrightness ?? brightness.GetBrightness)()));
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
    var foreground = ReadForegroundForDecision(config);
    var decision = PolicyEngine.Evaluate(idle, isOnAc, batteryPercent, foreground, config, nowLocal);
    var expected = decision.TargetPlanGuid;

    var bright = (readBrightness ?? brightness.GetBrightness)();
    var actionRecheckDenied = false;
    if (PolicyMutationAllowed() && decision.Source is not GuardDecisionSource.None
        && expected is not null && PolicyEngine.ShouldApplyPowerPlanSwitch(current, expected))
    {
      // WMI 查询期间用户可能切走或撤销配置。所有切前副作用和纠偏通知只能消费
      // 最新配置、空闲/供电与当前前台的同一决策；这里不承诺与系统切换绝对原子化。
      // 强制供电查询也可能回退 WMI，因此必须在最终授权读取之前完成所有慢探测。
      (batteryPercent, isOnAc) = (readBatteryInfo ?? BatteryInfoProvider.GetBatteryInfo)(true);
      observePowerSource(isOnAc);
      idle = (int)idleTracker.Sample(readIdleSeconds ?? IdleDetector.GetIdleSeconds);
      var latest = loadConfig();
      if (!latest.IsValid || latest.Config is null)
      {
        AbsorbPlanAsBaseline(state, current);
        state.LastForegroundAdmitted = false;
        publisher.Publish(CreateDegradedObservationPayload(
          state, time.GetUtcNow(), current, planCatalog, idle, isOnAc, batteryPercent, bright));
        return;
      }
      config = latest.Config;
      foreground = ReadForegroundForDecision(config);
      decision = PolicyEngine.Evaluate(idle, isOnAc, batteryPercent, foreground, config,
        time.GetLocalNow().LocalDateTime);
      expected = decision.TargetPlanGuid;
      actionRecheckDenied = decision.Source is GuardDecisionSource.None;
    }

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

    var externalChangeDetected = PolicyMutationAllowed() &&
      !context.AbsorbCurrentPlanAsBaseline &&
      !actionRecheckDenied &&
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
    LogBrightnessChangeIfNeeded(config, bright, state, writeLog);

    var planSwitched = false;
    string? switchedPlanName = null;
    var switchBrightnessAfter = bright;
    var switchBrightnessBefore = bright;

    if (PolicyMutationAllowed() &&
        decision.Source is not GuardDecisionSource.None &&
        expected is not null &&
        PolicyEngine.ShouldApplyPowerPlanSwitch(current, expected))
    {
      var result = await SwitchWithBrightnessLockAsync(expected.Value, bright, config, PolicyMutationAllowed, cancellationToken);
      switchBrightnessBefore = result.Before;
      switchBrightnessAfter = result.After;
      if (result.Apply is not PowerPlanApplyResult applied)
      {
        // 慢亮度同步期间暂停：尚未启动 setactive，保留观测状态等待恢复。
      }
      else if (!applied.Succeeded)
      {
        writeLog(
          config,
          LogLevel.Warn,
          $"电源计划切换失败：{applied.FailureKind} {applied.Diagnostic}",
          EventNameRegistry.PowerPlanApplyFailed);
      }
      else
      {
        state.ScriptJustSwitched = true;
        planSwitched = true;
        if (applied.ObservedGuid is Guid observedGuid)
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

    var (guardSource, guardReason) = ResolveGuardStatusCodes(decision, PolicyMutationAllowed());

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
      notificationEvent = state.NotificationRetention.Event,
      guardSource = guardSource,
      guardReason = guardReason,
      foregroundEventsAvailable = ResolveForegroundEventsAvailability(config),
    });
  }

  private ForegroundObservation ReadForegroundForDecision(GuardConfig config)
  {
    if (readForeground is null)
      return ForegroundObservation.Unknown;

    // 复用领域准入规则：空/非法名单无法受益于进程查询，周期补偿也应跳过。
    var admission = PolicyEngine.EvaluateAdmission(ForegroundObservation.Unknown, config);
    return admission.Reason is GuardDecisionReason.ForegroundListEmpty
      or GuardDecisionReason.ForegroundProgramPathsInvalid
      ? ForegroundObservation.Unknown
      : readForeground();
  }

  /// <summary>
  /// 决策 → 稳定代码：来源/原因经 <see cref="GuardDecisionCodes"/> 登记映射；
  /// 电源生命周期门禁阻止变更时（自动决策无法执行），原因改发 blocked-by-lifecycle，
  /// 不得把被门禁阻止的轮次显示为普通策略守护。
  /// </summary>
  private static (string Source, string Reason) ResolveGuardStatusCodes(
    GuardDecision decision,
    bool allowPolicyMutation)
  {
    var source = GuardDecisionCodes.ToSourceCode(decision.Source);
    var reason = GuardDecisionCodes.ToReasonCode(decision.Reason);
    if (decision.Source is GuardDecisionSource.Automatic && !allowPolicyMutation)
      reason = GuardStatusCodes.Reasons.BlockedByLifecycle;
    return (source, reason);
  }

  /// <summary>前台事件监听健康：限制关闭 = null（不适用）；开启时读取监听健康（false = 降级周期补偿）。</summary>
  private bool? ResolveForegroundEventsAvailability(GuardConfig config)
    => config.ForegroundRestrictionEnabled ? readForegroundEventsHealth?.Invoke() : null;

  private static void AbsorbPlanAsBaseline(GuardianLoopIterationState state, Guid? current)
  {
    state.LastKnownGuid = current;
    state.ScriptJustSwitched = false;
  }

  /// <summary>
  /// 配置不可用时的降级观测负载：不携带决策目标（expectedPlan 为空），
  /// 不推断暂停态；仅发布尽力而为的当前计划探测结果与健康采集值。
  /// 守护原因发布 config-unavailable，事件监听健康未知（null，不猜测）。
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
      notificationEvent = state.NotificationRetention.Event,
      guardSource = GuardStatusCodes.Sources.None,
      guardReason = GuardStatusCodes.Reasons.ConfigUnavailable,
      foregroundEventsAvailable = null,
    };

  /// <summary>
  /// 电源计划探测失败时的观测负载：配置可用（沿用其暂停语义与监听健康），
  /// 不携带决策目标，守护原因发布 probe-failed，UI 可区分"探测异常"与普通未命中。
  /// </summary>
  private StatusPayload CreateProbeFailedObservationPayload(
    GuardianLoopIterationState state,
    GuardConfig config,
    DateTimeOffset nowUtc,
    Guid? current,
    IReadOnlyDictionary<Guid, string>? planCatalog,
    int idle,
    bool isOnAc,
    int batteryPercent,
    int bright)
    => new()
    {
      timestamp = nowUtc.ToString("O"),
      currentPlan = DescribePlanWithoutConfig(current, planCatalog),
      currentPlanGUID = current?.ToString(),
      expectedPlan = null,
      idleSeconds = idle,
      isOnAC = isOnAc,
      batteryPercent = batteryPercent,
      brightness = bright,
      paused = config.Paused,
      enginePid = Environment.ProcessId,
      lastExternalChange = null,
      operationId = OperationIdContext.Current,
      notificationEvent = state.NotificationRetention.Event,
      guardSource = GuardStatusCodes.Sources.None,
      guardReason = GuardStatusCodes.Reasons.ProbeFailed,
      foregroundEventsAvailable = ResolveForegroundEventsAvailability(config),
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

  private async Task<(PowerPlanApplyResult? Apply, int Before, int After)> SwitchWithBrightnessLockAsync(
    Guid target,
    int brightnessBefore,
    GuardConfig config,
    Func<bool> canMutate,
    CancellationToken cancellationToken)
  {
    if (!canMutate()) return (null, brightnessBefore, brightnessBefore);
    if (powerCfgBrightnessSupported && brightnessBefore >= 0)
      (syncPlanBrightness ?? PowerCfgExecutor.SyncPlanBrightness)(target, brightnessBefore);

    if (!canMutate()) return (null, brightnessBefore, brightnessBefore);

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

    if (brightnessBefore < 0 || !canMutate()) return (apply, brightnessBefore, brightnessBefore);

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
