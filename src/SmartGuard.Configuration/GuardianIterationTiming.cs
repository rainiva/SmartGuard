namespace SmartGuard.Configuration;

public static class GuardianIterationTiming
{
  /// <summary>
  /// 前台事件轻路径准入复核节流下限（初始试验值，任务 3a 冻结；任务 3b 实测后收敛，
  /// 不对用户暴露调参项）。
  /// </summary>
  public static readonly TimeSpan ForegroundLightRecheckFloor = TimeSpan.FromSeconds(1);

  /// <summary>
  /// 前台命中转变触发的新增完整评估之间的最小间隔（初始试验值，任务 3a 冻结；
  /// 任务 3b 实测后收敛，不对用户暴露调参项）。
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
