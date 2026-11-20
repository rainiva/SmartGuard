using SmartGuard.Configuration;

namespace SmartGuard.Engine.Infrastructure;

/// <summary>Guardian power-plan operations on top of PowerSchemeService (PWR-INV-SCHEME-API).</summary>
public static class PowerCfgExecutor
{
  private static readonly PowerSource[] BothSources = [PowerSource.Ac, PowerSource.Dc];

  // Read-only capability probe: never writes a trial value. Only a confirmed absence (Win32 2) means
  // "unsupported"; any other failure is unknown and must not switch the guardian to WMI-only mode.
  internal static (BrightnessCapability Capability, PowerSchemeStatus Status) ProbeBrightnessCapability(Guid probePlanGuid)
  {
    var read = PowerSchemeService.ReadValueIndexAsync(probePlanGuid, PowerSchemeSetting.VideoNormalLevel, PowerSource.Ac)
      .GetAwaiter().GetResult();
    var capability = read.Succeeded ? BrightnessCapability.Supported
      : read.Status is { FailureKind: "api-error", Win32Error: 2 } ? BrightnessCapability.Unsupported
      : BrightnessCapability.ProbeFailed;
    return (capability, read.Status);
  }

  // Both return the first failed write, or a success status when every write succeeded.
  internal static PowerSchemeStatus SyncPlanBrightness(Guid planGuid, int brightness)
    => brightness is < 0 or > 100
      ? new PowerSchemeStatus("invalid-parameter", null, 0)
      : WriteBothSources(planGuid, PowerSchemeSetting.VideoNormalLevel, (uint)brightness);

  internal static PowerSchemeStatus DisableAdaptiveBrightness(Guid planGuid)
    => WriteBothSources(planGuid, PowerSchemeSetting.AdaptiveBrightness, 0);

  private static PowerSchemeStatus WriteBothSources(Guid planGuid, PowerSchemeSetting setting, uint value)
  {
    foreach (var source in BothSources)
    {
      var write = PowerSchemeService.WriteValueIndexAsync(planGuid, setting, source, value).GetAwaiter().GetResult();
      if (!write.Succeeded) return write.Status;
    }
    return new PowerSchemeStatus("none", null, 0);
  }

  internal static string DescribeSchemeFailure(PowerSchemeStatus status)
    => DescribeFailure(MapFailureKind(status), status.Win32Error);

  // Optional synchronization before switching: the target scheme takes effect with SetActive.
  internal static async Task SyncPlanBrightnessAsync(Guid planGuid, int brightness,
    CancellationToken cancellationToken, Action<string, PowerSchemeStatus>? observe = null,
    Func<bool>? canMutate = null)
  {
    if (brightness is < 0 or > 100) return;
    foreach (var source in new[] { PowerSource.Ac, PowerSource.Dc })
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (canMutate?.Invoke() == false) return;
      // The executor re-checks canMutate right before the native write.
      var result = await PowerSchemeService.WriteValueIndexAsync(planGuid, PowerSchemeSetting.VideoNormalLevel,
        source, (uint)brightness, canMutate, cancellationToken).ConfigureAwait(false);
      observe?.Invoke(source == PowerSource.Ac ? "plan-brightness-ac" : "plan-brightness-dc", result.Status);
      // Optional synchronization stops at the first failure or refusal.
      if (!result.Succeeded) return;
    }
  }

  // Guardian probe through PowerSchemeService (PWR-INV-SCHEME-API). A missing name is presentation only.
  internal static async Task<PowerPlanProbeResult> ProbeCurrentPlanAsync(CancellationToken cancellationToken,
    Action<PowerSchemeStatus>? observe = null)
  {
    var active = await PowerSchemeService.GetActiveAsync(cancellationToken).ConfigureAwait(false);
    observe?.Invoke(active.Status);
    if (!active.Succeeded)
      return new PowerPlanProbeResult(false, null, MapFailureKind(active.Status), active.Status.FailureKind, active.Status.Win32Error);
    var name = await PowerSchemeService.ReadNameAsync(active.Value, cancellationToken).ConfigureAwait(false);
    return new PowerPlanProbeResult(true, new PowerSchemeInfo(active.Value, name.Succeeded ? name.Value : null),
      PowerPlanFailureKind.None, string.Empty);
  }

  internal static async Task<PowerPlanApplyResult> ApplyAndVerifyAsync(
    Guid targetGuid,
    Func<Guid, CancellationToken, Task<PowerSchemeResult<bool>>> setActive,
    Func<CancellationToken, Task<PowerSchemeResult<Guid>>> getActive,
    Func<TimeSpan, CancellationToken, Task> delayAsync,
    CancellationToken cancellationToken)
  {
    var set = await setActive(targetGuid, cancellationToken).ConfigureAwait(false);
    if (!set.Succeeded)
      return new PowerPlanApplyResult(false, targetGuid, null, MapFailureKind(set.Status), set.Status.FailureKind, set.Status.Win32Error);

    Guid? observedGuid = null;
    var delays = new[] { TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(750) };
    for (var attempt = 0; attempt < 3; attempt++)
    {
      if (attempt > 0)
        await delayAsync(delays[attempt - 1], cancellationToken).ConfigureAwait(false);
      var probe = await getActive(cancellationToken).ConfigureAwait(false);
      if (!probe.Succeeded)
        return new PowerPlanApplyResult(false, targetGuid, null, MapFailureKind(probe.Status), probe.Status.FailureKind, probe.Status.Win32Error);
      observedGuid = probe.Value;
      if (observedGuid == targetGuid)
        return new PowerPlanApplyResult(true, targetGuid, observedGuid, PowerPlanFailureKind.None, string.Empty);
    }

    return new PowerPlanApplyResult(false, targetGuid, observedGuid, PowerPlanFailureKind.VerificationMismatch,
      $"Active scheme did not converge to {targetGuid:D}.");
  }

  internal static PowerPlanFailureKind MapFailureKind(PowerSchemeStatus status) => status.FailureKind switch
  {
    "call-pending" => PowerPlanFailureKind.CallPending,
    "timed-out" => PowerPlanFailureKind.TimedOut,
    "not-admitted" => PowerPlanFailureKind.NotAdmitted,
    _ => PowerPlanFailureKind.ApiFailed,
  };

  internal static string DescribeFailure(PowerPlanFailureKind kind, int? win32Error) => kind switch
  {
    PowerPlanFailureKind.ApiFailed => win32Error is int code ? $"电源服务调用失败（错误码 {code}）" : "电源服务调用失败",
    PowerPlanFailureKind.CallPending => "上次电源服务调用仍未返回，本轮未发起",
    PowerPlanFailureKind.TimedOut => "电源服务调用超时",
    PowerPlanFailureKind.VerificationMismatch => "切换后读回的计划不一致",
    PowerPlanFailureKind.NotAdmitted => "电源状态已变化，本次未执行",
    _ => kind.ToString(),
  };
}
