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

  // Catalog still runs through the process runner until Task 5 moves it to the scheme service.
  private void ReportProcessResult(string operation, PowerCfgCommandResult result, long budgetMs)
  {
    if (observeNativeProbe is null) return;
    var time = timeProvider ?? TimeProvider.System;
    var now = time.GetTimestamp();
    var hadFailure = _processFailures.TryGetValue(operation, out var previous);
    if (result.Succeeded)
    {
      if (!hadFailure) return;
      _processFailures.Remove(operation);
      SafeObserveNativeProbe(new(operation, "none", result.ElapsedMilliseconds, budgetMs,
        result.TerminationConfirmed, result.ExitCode, previous.Count, true));
      return;
    }
    var kind = result.FailureKind != "none" ? result.FailureKind
      : result.TimedOut ? "timed-out" : !result.Started ? "start-failed" : "command-failed";
    var count = hadFailure ? previous.Count + 1 : 1;
    var emit = !hadFailure || previous.Kind != kind || time.GetElapsedTime(previous.At, now) >= TimeSpan.FromMinutes(1);
    _processFailures[operation] = (kind, emit ? 0 : count, emit ? now : previous.At);
    if (emit) SafeObserveNativeProbe(new(operation, kind, result.ElapsedMilliseconds, budgetMs,
      result.TerminationConfirmed, result.ExitCode, count - 1, false));
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
    using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
    budget.CancelAfter(TimeSpan.FromSeconds(2));
    var time = timeProvider ?? TimeProvider.System;
    var started = time.GetTimestamp();
    PowerCfgCommandResult? last = null;
    PowerCfgCommandResult? firstFailure = null;
    var reportedFailure = false;
    try
    {
      var catalog = await PowerPlanCatalogProvider.TryLoadOnceAsync(budget.Token, result =>
      {
        last = result;
        if (budget.IsCancellationRequested && !token.IsCancellationRequested)
          result = result with { TimedOut = true, FailureKind = "timed-out" };
        if (!result.Succeeded) firstFailure ??= result;
        if (!result.TerminationConfirmed)
        {
          // Revoke the entire remaining enrichment chain, not only the next
          // normal plan read. A later hidden query cannot certify recovery.
          ReportProcessResult("plan-catalog", result, 2000);
          reportedFailure = true;
          budget.Cancel();
        }
      });
      token.ThrowIfCancellationRequested();
      if (!reportedFailure)
      {
        var aggregate = firstFailure ?? (catalog.Count > 0
          ? last ?? new PowerCfgCommandResult(false, false, 0, "", "")
          : new PowerCfgCommandResult(false, false, null, "", "") { FailureKind = "unavailable" });
        // A cached valid catalog needs no new child, but is a valid semantic result.
        if (catalog.Count > 0 && firstFailure is null) aggregate = aggregate with { Started = true };
        ReportProcessResult("plan-catalog", aggregate, 2000);
      }
      return catalog;
    }
    catch (OperationCanceledException) when (!token.IsCancellationRequested)
    {
      if (!reportedFailure) ReportProcessResult("plan-catalog", firstFailure ?? new PowerCfgCommandResult(true, true, null, "", "")
      { FailureKind = "timed-out", TerminationConfirmed = last?.TerminationConfirmed ?? false,
        ElapsedMilliseconds = (long)time.GetElapsedTime(started).TotalMilliseconds }, 2000);
      // Names are presentation only. Existing GUID-based policy and fallback
      // names work without finishing expensive hidden-scheme enrichment.
      return new Dictionary<Guid, string>();
    }
    catch (OperationCanceledException)
    {
      if (!reportedFailure) ReportProcessResult("plan-catalog", firstFailure ?? new PowerCfgCommandResult(false, false, null, "", "")
      { FailureKind = "cancelled", TerminationConfirmed = last?.TerminationConfirmed ?? false,
        ElapsedMilliseconds = (long)time.GetElapsedTime(started).TotalMilliseconds }, 2000);
      throw;
    }
  }
}
