using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

internal sealed partial class GuardianIterationRunner
{
  // Only fixed operation names below enter this bounded, main-round-owned map.
  // It stores aggregate failure evidence, never configuration or process facts.
  private readonly Dictionary<string, (string Kind, long Count, long At)> _processFailures = new();
  private bool _processExecutionClosed;
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
    var result = writeLog(config, LogLevel.Warn,
      $"电源计划探测失败，停止本轮切换：{probe.FailureKind}；合并省略 {_suppressedPlanProbeWarnings} 次同类异常。",
      EventNameRegistry.PowerPlanProbeFailed, new Dictionary<string, System.Text.Json.JsonElement>
      {
        ["failureKind"] = System.Text.Json.JsonSerializer.SerializeToElement(probe.FailureKind.ToString()),
        ["suppressedCount"] = System.Text.Json.JsonSerializer.SerializeToElement(_suppressedPlanProbeWarnings),
      }, null);
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
      : PowerCfgExecutor.ParseProbeResult(await RunObservedPowerCfgAsync("/getactivescheme", token));
    token.ThrowIfCancellationRequested();
    return value;
  }

  private async Task<PowerCfgCommandResult> RunObservedPowerCfgAsync(string arguments, CancellationToken token)
  {
    token.ThrowIfCancellationRequested();
    var result = _processExecutionClosed
      ? new PowerCfgCommandResult(false, false, null, "", "Process cleanup remains unconfirmed.")
        { FailureKind = "cleanup-unconfirmed", TerminationConfirmed = false }
      : await PowerCfgExecutor.RunPowerCfgAsync(arguments, token);
    var diagnostic = result.Succeeded && arguments == "/getactivescheme" && !PowerCfgExecutor.ParseProbeResult(result).Succeeded
      ? result with { FailureKind = "invalid-reply" } : result;
    ReportProcessResult(arguments.StartsWith("/setactive", StringComparison.Ordinal) ? "plan-apply" : "plan-read", diagnostic, 5000);
    token.ThrowIfCancellationRequested();
    return result;
  }

  private void ReportProcessResult(string operation, PowerCfgCommandResult result, long budgetMs)
  {
    _processExecutionClosed |= !result.TerminationConfirmed;
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
      // Diagnostics are optional; the probe result and cleanup latch remain
      // authoritative even when its consumer cannot accept an observation.
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
