namespace SmartGuard.Configuration;

public static class GuardianIterationTiming
{
  /// <summary>
  /// 前台事件轻路径准入复核节流下限（冻结值：1 秒；进程内常量，不对用户暴露调参项）。
  /// 受控对比与真实静止样本支持保留当前机制，尚无缩短下限的证据。
  /// 试验范围和限制见 .work/guard-rules/controlled-performance-report.md；
  /// 短时静止无额外业务工作不代表零成本、长期稳定或整机省电。
  /// </summary>
  public static readonly TimeSpan ForegroundLightRecheckFloor = TimeSpan.FromSeconds(1);

  /// <summary>
  /// 前台命中转变触发的新增完整评估之间的最小间隔（冻结值：5 秒；进程内常量，
  /// 不对用户暴露调参项）。试验证据范围同 <see cref="ForegroundLightRecheckFloor"/>；
  /// 提前识别不等于立即执行完整轮次，保持此下限限制切换场景的系统查询和IO。
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
