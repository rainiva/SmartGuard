using System.Text.RegularExpressions;
using SmartGuard.Configuration;

namespace SmartGuard.Engine.Infrastructure;

public static partial class PowerCfgExecutor
{
  private static readonly Regex GuidRegex = ActiveSchemeGuidPattern();

  public static Guid? ParseActiveSchemeGuid(string output)
  {
    var match = GuidRegex.Match(output);
    return match.Success ? Guid.Parse(match.Groups[1].Value) : null;
  }

  public static PowerSchemeInfo? ParseCurrentPlanInfo(string output)
  {
    foreach (Match match in PowerSchemeListPattern().Matches(output))
    {
      return new PowerSchemeInfo(
        Guid.Parse(match.Groups[1].Value),
        match.Groups[2].Value.Trim());
    }

    var guid = ParseActiveSchemeGuid(output);
    return guid is null ? null : new PowerSchemeInfo(guid.Value, null);
  }

  internal static PowerPlanProbeResult ParseProbeResult(PowerCfgCommandResult command)
  {
    if (!command.Started)
      return FailedProbe(PowerPlanFailureKind.ProcessStartFailed, command.StandardError);
    if (command.TimedOut)
      return FailedProbe(PowerPlanFailureKind.TimedOut, command.StandardError);
    if (!command.Succeeded)
      return FailedProbe(PowerPlanFailureKind.CommandFailed, command.StandardError);

    var plan = ParseCurrentPlanInfo(command.StandardOutput);
    return plan is null
      ? FailedProbe(PowerPlanFailureKind.ParseFailed, command.StandardError)
      : new PowerPlanProbeResult(true, plan, PowerPlanFailureKind.None, string.Empty);
  }

  internal static PowerPlanProbeResult ProbeCurrentPlan()
    => ParseProbeResult(PowerCfgProcessRunner.RunPowerCfg("/getactivescheme"));

  internal static async Task<PowerPlanProbeResult> ProbeCurrentPlanAsync(CancellationToken cancellationToken)
    => ParseProbeResult(await PowerCfgProcessRunner.RunPowerCfgAsync("/getactivescheme", cancellationToken));

  public static PowerSchemeInfo? GetCurrentPlanInfo() => ProbeCurrentPlan().Plan;

  public static Guid? GetCurrentPlanGuid() => ProbeCurrentPlan().ObservedGuid;

  public static Dictionary<Guid, string> ParsePowerSchemeList(string output)
  {
    var result = new Dictionary<Guid, string>();
    foreach (Match match in PowerSchemeListPattern().Matches(output))
    {
      var guid = Guid.Parse(match.Groups[1].Value);
      var name = match.Groups[2].Value.Trim();
      result[guid] = name;
    }
    return result;
  }

  public static PowerCfgCommandResult SetActivePlanResult(Guid planGuid)
    => PowerCfgProcessRunner.RunPowerCfg($"/setactive {planGuid:D}");

  public static void SetActivePlan(Guid planGuid)
    => _ = SetActivePlanResult(planGuid);

  internal static async Task<PowerPlanApplyResult> ApplyAndVerifyAsync(
    Guid targetGuid,
    Func<string, CancellationToken, Task<PowerCfgCommandResult>> runAsync,
    Func<TimeSpan, CancellationToken, Task> delayAsync,
    CancellationToken cancellationToken)
  {
    var setResult = await runAsync($"/setactive {targetGuid:D}", cancellationToken);
    if (!setResult.Succeeded)
      return FailedApply(targetGuid, setResult, MapFailureKind(setResult));

    Guid? observedGuid = null;
    var delays = new[]
    {
      TimeSpan.FromMilliseconds(250),
      TimeSpan.FromMilliseconds(750),
    };

    for (var attempt = 0; attempt < 3; attempt++)
    {
      if (attempt > 0)
        await delayAsync(delays[attempt - 1], cancellationToken);

      var probeCommand = await runAsync("/getactivescheme", cancellationToken);
      var probe = ParseProbeResult(probeCommand);
      if (!probe.Succeeded)
        return new PowerPlanApplyResult(
          false,
          targetGuid,
          probe.ObservedGuid,
          probe.FailureKind,
          probe.Diagnostic);

      observedGuid = probe.ObservedGuid;
      if (observedGuid == targetGuid)
      {
        return new PowerPlanApplyResult(
          true,
          targetGuid,
          observedGuid,
          PowerPlanFailureKind.None,
          string.Empty);
      }
    }

    return new PowerPlanApplyResult(
      false,
      targetGuid,
      observedGuid,
      PowerPlanFailureKind.VerificationMismatch,
      $"powercfg active scheme did not converge to {targetGuid:D}.");
  }

  internal static Task<PowerCfgCommandResult> RunPowerCfgAsync(
    string arguments,
    CancellationToken cancellationToken)
    => PowerCfgProcessRunner.RunPowerCfgAsync(arguments, cancellationToken);

  public static bool IsBrightnessSupported(Guid probePlanGuid)
  {
    var result = PowerCfgProcessRunner.RunPowerCfg(
      $"/setacvalueindex {probePlanGuid:D} SUB_VIDEO VIDEONORMALLEVEL 50");
    return result.Succeeded && !ContainsPowerCfgError(result.CombinedOutput);
  }

  public static void SyncPlanBrightness(Guid planGuid, int brightness)
  {
    if (brightness < 0 || brightness > 100) return;
    PowerCfgProcessRunner.RunPowerCfg(
      $"/setacvalueindex {planGuid:D} SUB_VIDEO VIDEONORMALLEVEL {brightness}");
    PowerCfgProcessRunner.RunPowerCfg(
      $"/setdcvalueindex {planGuid:D} SUB_VIDEO VIDEONORMALLEVEL {brightness}");
  }

  internal static async Task SyncPlanBrightnessAsync(Guid planGuid, int brightness,
    CancellationToken cancellationToken, Action<string, PowerCfgCommandResult>? observe = null,
    Func<bool>? canMutate = null)
  {
    if (brightness < 0 || brightness > 100) return;
    using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    budget.CancelAfter(TimeSpan.FromSeconds(2));
    foreach (var mode in new[] { "ac", "dc" })
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (budget.IsCancellationRequested || canMutate?.Invoke() == false) return;
      var result = await PowerCfgProcessRunner.RunPowerCfgAsync(
        $"/set{mode}valueindex {planGuid:D} SUB_VIDEO VIDEONORMALLEVEL {brightness}", budget.Token);
      if (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        result = result with { TimedOut = true, FailureKind = "timed-out" };
      observe?.Invoke("plan-brightness-" + mode, result);
      cancellationToken.ThrowIfCancellationRequested();
      // Optional synchronization cannot spend another command budget after failure.
      if (!result.Succeeded) return;
    }
  }

  public static void DisableAdaptiveBrightness(Guid planGuid)
  {
    PowerCfgProcessRunner.RunPowerCfg($"/setacvalueindex {planGuid:D} SUB_VIDEO ADAPTBRIGHT 0");
    PowerCfgProcessRunner.RunPowerCfg($"/setdcvalueindex {planGuid:D} SUB_VIDEO ADAPTBRIGHT 0");
  }

  private static PowerPlanProbeResult FailedProbe(PowerPlanFailureKind kind, string diagnostic)
    => new(false, null, kind, diagnostic);

  private static PowerPlanApplyResult FailedApply(
    Guid targetGuid,
    PowerCfgCommandResult command,
    PowerPlanFailureKind kind)
    => new(false, targetGuid, null, kind, command.StandardError);

  private static PowerPlanFailureKind MapFailureKind(PowerCfgCommandResult command)
  {
    if (!command.Started) return PowerPlanFailureKind.ProcessStartFailed;
    if (command.TimedOut) return PowerPlanFailureKind.TimedOut;
    return PowerPlanFailureKind.CommandFailed;
  }

  private static bool ContainsPowerCfgError(string text)
    => text.Contains("不存在", StringComparison.Ordinal)
      || text.Contains("无效", StringComparison.Ordinal)
      || text.Contains("invalid", StringComparison.OrdinalIgnoreCase)
      || text.Contains("not exist", StringComparison.OrdinalIgnoreCase);

  [GeneratedRegex(@"GUID:\s+([0-9a-fA-F-]{36})", RegexOptions.IgnoreCase)]
  private static partial Regex ActiveSchemeGuidPattern();

  [GeneratedRegex(@"GUID:\s+([0-9a-fA-F-]{36})\s+\(([^)]+)\)", RegexOptions.IgnoreCase)]
  private static partial Regex PowerSchemeListPattern();

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
