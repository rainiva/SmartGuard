namespace SmartGuard.Configuration;

internal static class HighPerformanceBoost
{
  public static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(1);

  public static void Apply(
    GuardConfigRepository repository,
    string root,
    IPowerPlanActivator activator,
    TimeSpan? duration = null)
  {
    var config = repository.LoadOrDefault(root);
    var until = DateTime.Now.Add(duration ?? DefaultDuration);
    var mutations = new ConfigMutationService(repository);
    mutations.SetManualHighPerformanceUntil(until);
    var activation = activator.SetActivePlan(config.ActivePlanGuid);
    repository.AppendInfoLog(
      ManualHighPerformanceMessages.FormatApplied(until),
      root,
      SmartGuardPaths.StartupLogFile(root));
    if (!activation.Succeeded)
      repository.AppendInfoLog(
        ManualHighPerformanceMessages.FormatActivationDeferred(DescribeActivationFailure(activation)),
        root,
        SmartGuardPaths.StartupLogFile(root));
  }

  // Mirrors the engine's PowerCfgExecutor.DescribeFailure labels (Configuration cannot reference Engine);
  // the engine's "本轮未发起" is left out because the tray switch has no guardian rounds.
  private static string DescribeActivationFailure(PowerSchemeStatus status) => status.FailureKind switch
  {
    "timed-out" => "电源服务调用超时",
    "call-pending" => "上次电源服务调用仍未返回",
    _ => status.Win32Error is int code ? $"电源服务调用失败（错误码 {code}）" : "电源服务调用失败",
  };
}
