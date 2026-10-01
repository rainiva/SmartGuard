using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

/// <summary>
/// 单个守护循环周期的唤醒分类与节流 Owner：
/// - 完整周期截止时间唯一权威：截止 = 最近一次完整轮次 + 配置间隔；前台事件不推迟截止时间，
///   持续事件不得饿死周期轮次；
/// - 唯一待处理前台标记：0→1 转换唤醒 Engine（级别触发：处理期间的信号保留为待处理，
///   最后一个信号永不丢失）；事件只表示需要重新核验当前前台，不重放历史窗口；
/// - 轻路径：准入复核至多每秒一次；命中转变请求受限完整评估（新增完整评估之间至少间隔 5 秒，
///   被节流拦下的请求在节流结束后必定补跑——末次稳定前台须在节流结束后重新评估）；
/// - 暂停 / 人工接管 / 限制关闭 / 名单为空 / 名单非法 / 配置不可用时跳过业务查询，
///   仅保留受控订阅，状态变化按原周期重新检查。
/// 准入判定经 <see cref="PolicyEngine.EvaluateAdmission"/> 单一规则真源；本类不查询
/// 电源计划、不做 WMI、不写状态、不写日志，完整评估经注入委托由 GuardianIterationRunner 执行。
/// </summary>
internal sealed class GuardianCycleCoordinator
{
  private readonly Func<GuardConfigReadResult> _loadConfig;
  private readonly Func<ForegroundObservation>? _readForeground;
  private readonly GuardianLoopIterationState _state;
  private readonly SemaphoreSlim _wakeSignal;
  private readonly Func<GuardianIterationContext, CancellationToken, Task> _runFullIteration;
  private readonly TimeProvider _time;
  private readonly Func<bool> _consumePowerWake;
  private readonly TimeSpan _lightRecheckFloor;
  private readonly TimeSpan _fullEvaluationFloor;

  private int _foregroundPending;
  private long _nextPeriodicDeadline;
  private long? _lastLightRecheck;
  private long? _lastFullEvaluation;
  private bool _fullEvaluationPending;
  private bool _recheckAfterGateLift;

  public GuardianCycleCoordinator(
    Func<GuardConfigReadResult> loadConfig,
    Func<ForegroundObservation>? readForeground,
    GuardianLoopIterationState state,
    SemaphoreSlim wakeSignal,
    Func<GuardianIterationContext, CancellationToken, Task> runFullIteration,
    TimeProvider? timeProvider = null,
    Func<bool>? consumePowerWake = null,
    TimeSpan? lightRecheckFloor = null,
    TimeSpan? fullEvaluationFloor = null)
  {
    _loadConfig = loadConfig;
    _readForeground = readForeground;
    _state = state;
    _wakeSignal = wakeSignal;
    _runFullIteration = runFullIteration;
    _time = timeProvider ?? TimeProvider.System;
    _consumePowerWake = consumePowerWake ?? (() => false);
    _lightRecheckFloor = lightRecheckFloor ?? GuardianIterationTiming.ForegroundLightRecheckFloor;
    _fullEvaluationFloor = fullEvaluationFloor ?? GuardianIterationTiming.ForegroundFullEvaluationFloor;
    _nextPeriodicDeadline = _time.GetTimestamp();
  }

  /// <summary>
  /// 监听线程回调：仅做 0→1 标记转换并在转换时唤醒 Engine；无任何 IO。
  /// 处理期间新到的信号保持待处理（级别触发），最后一个信号不会丢失。
  /// </summary>
  public void MarkForegroundSignal()
  {
    if (Interlocked.Exchange(ref _foregroundPending, 1) == 0)
    {
      try
      {
        _wakeSignal.Release();
      }
      catch
      {
        // SemaphoreSlim 上限为 int.MaxValue，不会满；防御性收口避免监听线程抛异常。
      }
    }
  }

  public bool HasPendingForegroundSignal => Volatile.Read(ref _foregroundPending) == 1;

  /// <summary>
  /// 执行一个循环周期：按唤醒来源分类（电源事件 / 周期截止 / 待补完整评估 → 完整轮次；
  /// 前台信号 → 节流后的轻路径）。完整轮次重排周期截止时间；前台事件只走轻路径，
  /// 不推迟截止时间。挂起阶段（RunIteration=false）不执行任何轮次或前台工作，
  /// 信号保留待恢复后由完整轮次吸收。
  /// </summary>
  public async Task RunCycleAsync(
    PowerLifecycleDirective directive,
    int intervalSeconds,
    CancellationToken cancellationToken)
  {
    var powerWake = _consumePowerWake();
    if (!directive.RunIteration)
      return;

    var now = _time.GetTimestamp();
    var foregroundPending = Interlocked.Exchange(ref _foregroundPending, 0) == 1;

    if (powerWake || now >= _nextPeriodicDeadline || IsFullEvaluationDue(now))
    {
      await RunFullEvaluationAsync(directive, intervalSeconds, cancellationToken);
      return;
    }

    if ((foregroundPending || _recheckAfterGateLift)
        && ElapsedSince(_lastLightRecheck, now) >= _lightRecheckFloor.Ticks)
    {
      _recheckAfterGateLift = false;
      _lastLightRecheck = now;
      ProcessForegroundLight();

      // 轻路径发现命中转变且节流已过：同一周期内立即执行完整评估，避免额外唤醒延迟。
      if (IsFullEvaluationDue(now))
        await RunFullEvaluationAsync(directive, intervalSeconds, cancellationToken);
      return;
    }

    if (foregroundPending)
    {
      // 轻路径节流窗口内：保留最新待处理标记，节流到期后补处理（末次信号不丢失）。
      Interlocked.Exchange(ref _foregroundPending, 1);
    }
  }

  /// <summary>
  /// 距下一次应被唤醒的时长：完整周期截止、待处理前台信号的节流到期、待补完整评估的节流到期，取最小者。
  /// </summary>
  public TimeSpan ComputeWaitDelay()
  {
    var now = _time.GetTimestamp();
    var delayTicks = _time.GetElapsedTime(now, _nextPeriodicDeadline).Ticks;

    if (HasPendingForegroundSignal)
    {
      var lightRemaining = _lightRecheckFloor.Ticks - ElapsedSince(_lastLightRecheck, now);
      if (lightRemaining > 0)
        delayTicks = Math.Min(delayTicks, lightRemaining);
    }

    if (_fullEvaluationPending)
    {
      var fullRemaining = _fullEvaluationFloor.Ticks - ElapsedSince(_lastFullEvaluation, now);
      delayTicks = Math.Min(delayTicks, Math.Max(0, fullRemaining));
    }

    return delayTicks <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks(delayTicks);
  }

  /// <summary>
  /// 周期异常失败后的收敛：按完整间隔冷却重试（不紧密循环），丢弃节流中的前台请求，
  /// 下一真实前台事件会重新标记；周期复核是兜底真源。
  /// </summary>
  public void NoteCycleFailure(int intervalSeconds)
  {
    _nextPeriodicDeadline = NextPeriodicDeadline(_time.GetTimestamp(), intervalSeconds);
    _fullEvaluationPending = false;
    _recheckAfterGateLift = false;
    Interlocked.Exchange(ref _foregroundPending, 0);
  }

  private async Task RunFullEvaluationAsync(
    PowerLifecycleDirective directive,
    int intervalSeconds,
    CancellationToken cancellationToken)
  {
    var context = new GuardianIterationContext(
      directive.AllowPolicyMutation,
      directive.ResetIdleEpoch,
      directive.ForcePowerRefresh,
      directive.AbsorbCurrentPlanAsBaseline,
      directive.Phase);
    await _runFullIteration(context, cancellationToken);

    var now = _time.GetTimestamp();
    _nextPeriodicDeadline = NextPeriodicDeadline(now, intervalSeconds);
    _lastFullEvaluation = now;
    _fullEvaluationPending = false;
    _recheckAfterGateLift = false;
  }

  /// <summary>
  /// 前台事件轻路径：仅做准入状态比较。准入结果未变 → 立即结束（不查电源计划/WMI、
  /// 不写状态、不写日志）；命中→未命中 → 立即撤销自动动作资格（基线由下一完整轮次吸收，
  /// 轻路径不探测 PowerCfg）；未命中→命中 → 请求受限完整评估（基线吸收与准入历史推进
  /// 由完整轮次的统一决策完成）。
  /// </summary>
  private void ProcessForegroundLight()
  {
    var configRead = _loadConfig();
    if (!configRead.IsValid || configRead.Config is not { } config)
      return;

    var nowLocal = _time.GetLocalNow().LocalDateTime;
    if (config.Paused || config.IsManualHighPerformanceActive(nowLocal))
    {
      _recheckAfterGateLift = true;
      return;
    }

    if (!config.ForegroundRestrictionEnabled)
      return;

    // 先让规则真源判断无需身份的拒绝条件，空/非法名单不能产生无用进程查询。
    var admission = PolicyEngine.EvaluateAdmission(ForegroundObservation.Unknown, config);
    if (admission.Reason is GuardDecisionReason.ForegroundListEmpty
      or GuardDecisionReason.ForegroundProgramPathsInvalid)
    {
      _recheckAfterGateLift = true;
      return;
    }

    var foreground = _readForeground?.Invoke() ?? ForegroundObservation.Unknown;
    admission = PolicyEngine.EvaluateAdmission(foreground, config);

    var previouslyAdmitted = _state.LastForegroundAdmitted ?? false;
    if (previouslyAdmitted == admission.Admitted)
      return;

    if (admission.Admitted)
    {
      _fullEvaluationPending = true;
      return;
    }

    _state.LastForegroundAdmitted = false;
  }

  private bool IsFullEvaluationDue(long now)
    => _fullEvaluationPending && ElapsedSince(_lastFullEvaluation, now) >= _fullEvaluationFloor.Ticks;

  // Raw timestamp units belong to the provider, not TimeSpan (10MHz is only a
  // coincidence on some hosts). Null means the first evaluation is unrestricted.
  private long ElapsedSince(long? since, long now)
    => since is long start ? _time.GetElapsedTime(start, now).Ticks : TimeSpan.MaxValue.Ticks;

  private long NextPeriodicDeadline(long now, int intervalSeconds)
    => checked(now + (long)Math.Max(1, intervalSeconds) * _time.TimestampFrequency);
}
