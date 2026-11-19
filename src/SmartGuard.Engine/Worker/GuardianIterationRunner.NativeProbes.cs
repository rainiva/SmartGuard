using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

internal sealed partial class GuardianIterationRunner
{
  // Only fixed operation names below enter this bounded, main-round-owned map.
  // It stores aggregate failure evidence, never configuration or process facts.
  private readonly Dictionary<string, (string Kind, long Count, long At)> _processFailures = new();
  private PowerPlanFailureKind? _lastPlanProbeFailure;
  private long _lastPlanProbeWarningAt, _suppressedPlanProbeWarnings;

  private void LogPlanProbeFailure(GuardConfig config, PowerPlanProbeResult probe)
  {
    var time = timeProvider ?? TimeProvider.System;
    var now = time.GetTimestamp();
    if (_lastPlanProbeFailure == probe.FailureKind
        && time.GetElapsedTime(_lastPlanProbeWarningAt, now) < TimeSpan.FromMinutes(1))
    {
      _suppressedPlanProbeWarnings++;
      return;
    }
    var properties = new Dictionary<string, JsonElement>
    {
      ["failureKind"] = JsonSerializer.SerializeToElement(probe.FailureKind.ToString()),
      ["suppressedCount"] = JsonSerializer.SerializeToElement(_suppressedPlanProbeWarnings),
    };
    if (probe.Win32Error is int code) properties["win32Error"] = JsonSerializer.SerializeToElement(code);
    var result = writeLog(config, LogLevel.Warn,
      $"电源计划探测失败，停止本轮切换：{PowerCfgExecutor.DescribeFailure(probe.FailureKind, probe.Win32Error)}；合并省略 {_suppressedPlanProbeWarnings} 次同类异常。",
      EventNameRegistry.PowerPlanProbeFailed, properties, null);
    if (result.IsAccepted() || result == GuardianLogWriteResult.SuppressedByLevel)
    {
      _lastPlanProbeFailure = probe.FailureKind;
      _lastPlanProbeWarningAt = now;
      _suppressedPlanProbeWarnings = 0;
    }
  }

  private int ReadBrightness(CancellationToken token)
  {
    token.ThrowIfCancellationRequested();
    var value = readBrightness is null ? brightness.GetBrightness(token) : readBrightness();
    token.ThrowIfCancellationRequested();
    return value;
  }

  private (int Percent, bool IsOnAc) ReadBattery(bool force, CancellationToken token)
  {
    token.ThrowIfCancellationRequested();
    var value = readBatteryInfo is null ? BatteryInfoProvider.GetBatteryInfo(force, token, nativeProbe) : readBatteryInfo(force);
    token.ThrowIfCancellationRequested();
    return value;
  }

  private async Task<PowerPlanProbeResult> ReadCurrentPlanAsync(CancellationToken token)
  {
    token.ThrowIfCancellationRequested();
    var value = readCurrentPlan is not null ? readCurrentPlan()
      : await PowerCfgExecutor.ProbeCurrentPlanAsync(token, status => ReportSchemeStatus("plan-read", status));
    token.ThrowIfCancellationRequested();
    return value;
  }

  // admit is re-evaluated by the executor right before the native call: lifecycle revocation is a
  // predicate, not a cancellation, so the caller's own earlier check is not enough.
  private async Task<PowerSchemeResult<bool>> SetActiveObservedAsync(Guid target, Func<bool> admit, CancellationToken token)
  {
    var result = await PowerSchemeService.SetActiveAsync(target, admit, token);
    ReportSchemeStatus("plan-apply", result.Status);
    return result;
  }

  private async Task<PowerSchemeResult<Guid>> GetActiveObservedAsync(CancellationToken token)
  {
    var result = await PowerSchemeService.GetActiveAsync(token);
    ReportSchemeStatus("plan-read", result.Status);
    return result;
  }

  // A scheme API call leaves no child process behind, so TerminationConfirmed is always true here.
  private void ReportSchemeStatus(string operation, PowerSchemeStatus status)
  {
    // A lifecycle refusal is neither a failure nor evidence of recovery.
    if (observeNativeProbe is null || status.FailureKind == "not-admitted") return;
    var budgetMs = (long)(operation == "plan-catalog"
      ? PowerSchemeService.EnumerateDeadline : PowerSchemeService.DefaultDeadline).TotalMilliseconds;
    var time = timeProvider ?? TimeProvider.System;
    var now = time.GetTimestamp();
    var hadFailure = _processFailures.TryGetValue(operation, out var previous);
    if (status.Succeeded)
    {
      if (!hadFailure) return;
      _processFailures.Remove(operation);
      SafeObserveNativeProbe(new(operation, "none", status.ElapsedMilliseconds, budgetMs, true, null, previous.Count, true));
      return;
    }
    var kind = status.FailureKind;
    var count = hadFailure ? previous.Count + 1 : 1;
    var emit = !hadFailure || previous.Kind != kind || time.GetElapsedTime(previous.At, now) >= TimeSpan.FromMinutes(1);
    _processFailures[operation] = (kind, emit ? 0 : count, emit ? now : previous.At);
    if (emit) SafeObserveNativeProbe(new(operation, kind, status.ElapsedMilliseconds, budgetMs,
      true, null, count - 1, false, status.Win32Error));
  }

  private void SafeObserveNativeProbe(WmiProbeObservation observation)
  {
    try { observeNativeProbe?.Invoke(observation); }
    catch
    {
      // Diagnostics are optional; the probe result remains authoritative even when
      // its consumer cannot accept an observation.
    }
  }

  private async Task<IReadOnlyDictionary<Guid, string>> LoadPlanCatalogAsync(CancellationToken token)
  {
    PowerSchemeStatus? firstFailure = null;
    var catalog = await PowerPlanCatalogProvider.TryLoadOnceAsync(token, status =>
    {
      if (!status.Succeeded) firstFailure ??= status;
    });
    token.ThrowIfCancellationRequested();
    // One aggregate outcome per load keeps per-operation recovery semantics.
    ReportSchemeStatus("plan-catalog", firstFailure ?? new PowerSchemeStatus("none", null, 0));
    return catalog;
  }
}
