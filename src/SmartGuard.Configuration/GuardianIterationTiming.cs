namespace SmartGuard.Configuration;

public static class GuardianIterationTiming
{
  /// <summary>
  /// 前台事件轻路径准入复核节流下限（冻结值：1 秒；进程内常量，不对用户暴露调参项）。
  /// 任务 3b 有界对比试验（证据：.work/guard-rules/trial-verdict.md）：静止场景候选 B
  /// （事件+周期补偿）与原周期采样完全等价——完整评估/电源探测/状态写入/日志零增加、
  /// 零自唤醒、无资源增长；合成信号机制路径延迟中位约 1.1ms（远低于本下限）。
  /// 真实切换端到端延迟因试验机处于锁定会话未完成实测（NOT RUN，无调整证据），维持初始值。
  /// </summary>
  public static readonly TimeSpan ForegroundLightRecheckFloor = TimeSpan.FromSeconds(1);

  /// <summary>
  /// 前台命中转变触发的新增完整评估之间的最小间隔（冻结值：5 秒；进程内常量，
  /// 不对用户暴露调参项）。任务 3b 试验证据与调整依据同 <see cref="ForegroundLightRecheckFloor"/>：
  /// 静止成本为零已实测；切换场景的完整评估频率收益未获端到端实测，无调整证据，维持初始值。
  /// </summary>
  public static readonly TimeSpan ForegroundFullEvaluationFloor = TimeSpan.FromSeconds(5);

  public static int ResolveWaitSeconds(GuardConfigRepository repository, string root)
    => ResolveWaitSeconds(repository, root, lifecycleReevaluateAfter: null);

  public static int ResolveWaitSeconds(
    GuardConfigRepository repository,
    string root,
    TimeSpan? lifecycleReevaluateAfter)
  {
    var configured = Math.Max(5, repository.LoadOrDefault(root).CheckIntervalSec);
    if (lifecycleReevaluateAfter is not TimeSpan remaining)
      return configured;

    var deadline = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
    return Math.Min(configured, deadline);
  }
}
