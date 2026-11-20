using SmartGuard.Configuration;

namespace SmartGuard.Engine.Infrastructure;

internal sealed record WmiProbeObservation(string Operation, string FailureKind,
  long ElapsedMilliseconds, long BudgetMilliseconds, bool TerminationConfirmed,
  int? ExitCode, long SuppressedCount, bool Recovered, int? Win32Error = null,
  string? CleanupEvidence = null, string? PendingCleanupEvidence = null);

internal sealed class WmiProbeProcess
{
  internal static WmiProbeProcess Shared { get; } = new();
  private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);
  internal const int CancellationCleanupGraceMilliseconds = 500;
  private readonly Func<string, string, TimeSpan, CancellationToken, Task<PowerCfgCommandResult>> _execute;
  private readonly TimeProvider _time;
  private readonly Action<WmiProbeObservation>? _observe;
  private readonly object _sync = new();
  private bool _busy;
  // At most one child whose cleanup is unproven; while held, no new child starts (PWR-INV-CLEANUP-REOPEN).
  private UnconfirmedChild? _pending;
  private long _pendingSince;
  // Rejections while a child is held are their own fact and never touch the failure cooldown in _failures.
  private readonly Dictionary<string, RejectionState> _rejections = new(3);
  private sealed record RejectionState(long ReportedAt, long Suppressed);
  // The command vocabulary is fixed at three operations, so health state cannot
  // grow with input. Success only recovers that same operation's failure.
  private readonly Dictionary<string, FailureState> _failures = new(3);
  private sealed record FailureState(string Kind, long ReportedAt, long FailedAt, long Elapsed,
    bool Terminated, int? ExitCode, long Suppressed);

  internal WmiProbeProcess(
    Func<string, string, TimeSpan, CancellationToken, Task<PowerCfgCommandResult>>? execute = null,
    TimeProvider? timeProvider = null, Action<WmiProbeObservation>? observe = null)
  {
    _execute = execute ?? ((file, args, budget, token) =>
      PowerCfgProcessRunner.RunProcessAsync(file, args, budget, token, retainUnconfirmed: true));
    _time = timeProvider ?? TimeProvider.System;
    _observe = observe;
  }

  internal bool HasPendingCleanupForTests { get { lock (_sync) return _pending is not null; } }

  internal int ReadBrightness(CancellationToken cancellationToken = default)
    => Run("brightness-read", null, cancellationToken) is { } reply ? int.Parse(reply[2], System.Globalization.CultureInfo.InvariantCulture) : -1;

  internal void SetBrightness(int level, CancellationToken cancellationToken = default)
  {
    if (level is >= 0 and <= 100) Run("brightness-write", level, cancellationToken);
  }

  internal (int Percent, bool? IsOnAc)? ReadBattery(CancellationToken cancellationToken = default)
  {
    var reply = Run("battery-read", null, cancellationToken);
    return reply is null ? null : (int.Parse(reply[2], System.Globalization.CultureInfo.InvariantCulture),
      reply[3] == "unknown" ? null : reply[3] == "ac");
  }

  private string[]? Run(string operation, int? level, CancellationToken token)
  {
    if (token.IsCancellationRequested) return null;
    if (!TryAcquire(operation)) return null;
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    try
    {
      if (token.IsCancellationRequested) return null;
      var executable = Path.ChangeExtension(typeof(Program).Assembly.Location, ".exe");
      var arguments = "--wmi-probe " + operation + (level is { } value ? " " + value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
      var pending = _execute(executable, arguments, Budget, token);
      PowerCfgCommandResult result;
      try
      {
        // The executor owns termination. This outer guard closes the boundary if that
        // owner ever fails to return; it cannot authorize another child or a late result.
        result = pending.WaitAsync(Budget + TimeSpan.FromMilliseconds(500), token).GetAwaiter().GetResult();
      }
      catch (TimeoutException)
      {
        _ = pending.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        Fail(operation, "cleanup-unconfirmed", 2500, false, null, "executor-pending", UnconfirmedChild.FromExecution(pending));
        return null;
      }
      catch (OperationCanceledException)
      {
        // Business cancellation discards the reply, but the same execution still
        // owns cleanup. A bounded certificate grace avoids disabling unrelated
        // probes while a correctly cancelling executor is finishing termination.
        PowerCfgCommandResult? cleanup = null;
        try
        {
          cleanup = pending.WaitAsync(TimeSpan.FromMilliseconds(CancellationCleanupGraceMilliseconds))
            .GetAwaiter().GetResult();
        }
        catch
        {
          // Timeout or executor fault cannot certify termination; observe any
          // later fault without reopening spawning over an uncertain child.
          _ = pending.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
        var elapsed = (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (cleanup is not null)
          Fail(operation, "cancelled", elapsed, cleanup.TerminationConfirmed, cleanup.ExitCode,
            cleanup.CleanupEvidence, cleanup.PendingCleanup);
        else
          Fail(operation, "cancelled", elapsed, false, null, "executor-pending", UnconfirmedChild.FromExecution(pending));
        return null;
      }
      if (token.IsCancellationRequested)
      {
        Fail(operation, "cancelled", result.ElapsedMilliseconds, result.TerminationConfirmed, result.ExitCode,
          result.CleanupEvidence, result.PendingCleanup);
        return null;
      }
      if (!result.Succeeded || !result.TerminationConfirmed || result.FailureKind != "none")
      {
        Fail(operation, result.FailureKind == "none" ? SafeChildError(result) : result.FailureKind,
          result.ElapsedMilliseconds, result.TerminationConfirmed, result.ExitCode,
          result.CleanupEvidence, result.PendingCleanup);
        return null;
      }
      var fields = result.StandardOutput.Length <= 128
        ? result.StandardOutput.TrimEnd('\r', '\n').Split('|') : [];
      if (result.StandardOutput.Length > 128 || result.StandardError.Length != 0 ||
          !ValidReply(operation, fields))
      {
        Fail(operation, "invalid-reply", result.ElapsedMilliseconds, true, result.ExitCode);
        return null;
      }
      WmiProbeObservation? recovery = null;
      lock (_sync)
      {
        if (_failures.Remove(operation, out var previous)) recovery = Observation(operation, "none", result.ElapsedMilliseconds, true, result.ExitCode, true, previous.Suppressed);
      }
      Publish(recovery);
      return fields;
    }
    // An unexpected thrown executor result has no cleanup certificate. Closing
    // spawning is safer than assuming cancellation killed a potentially live child.
    catch (OperationCanceledException) { Fail(operation, "cancelled", 0, false, null, "unprovable"); return null; }
    catch { Fail(operation, "io-failed", 0, false, null, "unprovable"); return null; }
    finally { lock (_sync) _busy = false; }
  }

  // Admission is one ownership decision (PWR-INV-CLEANUP-REOPEN). Under one lock: a busy slot rejects;
  // otherwise this request takes the slot. If a child is held, the slot is kept while the constant-cost
  // proof runs outside the lock, and a proven reopen hands the same slot straight to this request's
  // execution. The slot is never released between the held-child check and the start of a new child, so
  // no other request can leave a child in between.
  private bool TryAcquire(string operation)
  {
    UnconfirmedChild? pending;
    WmiProbeObservation? first = null, second = null;
    lock (_sync)
    {
      if (_busy || (_pending is null && InCooldown(operation)))
      {
        first = CountRejectedFailure(operation);
        pending = null;
      }
      else
      {
        _busy = true;
        pending = _pending;
        if (pending is null) return true;
      }
    }
    if (pending is null)
    {
      Publish(first);
      return false;
    }

    bool confirmed;
    try { confirmed = pending.TryConfirm(); }
    catch { confirmed = false; }
    var acquired = false;
    lock (_sync)
    {
      if (confirmed)
      {
        _pending = null;
        _rejections.Clear();
        first = new("process-cleanup", "none", (long)_time.GetElapsedTime(_pendingSince).TotalMilliseconds,
          (long)Budget.TotalMilliseconds, true, null, 0, true, CleanupEvidence: "confirmed");
        // Reopening does not lift this operation's own failure cooldown.
        if (InCooldown(operation)) second = CountRejectedFailure(operation);
        else acquired = true;   // keep the slot for this request's execution
      }
      else first = RecordRejection(operation, pending.Evidence);
      if (!acquired) _busy = false;
    }
    Publish(first);
    Publish(second);
    return acquired;
  }

  private bool InCooldown(string operation)
    => _failures.TryGetValue(operation, out var failed) && _time.GetElapsedTime(failed.FailedAt) < TimeSpan.FromSeconds(30);

  // Unchanged behaviour of the old admission block: a rejected request only counts toward this
  // operation's own failure summary (never another operation's), reported at most every 60 seconds.
  private WmiProbeObservation? CountRejectedFailure(string operation)
  {
    if (!_failures.TryGetValue(operation, out var failed)) return null;
    WmiProbeObservation? summary = null;
    failed = failed with { Suppressed = failed.Suppressed + 1 };
    if (_time.GetElapsedTime(failed.ReportedAt) >= TimeSpan.FromSeconds(60))
    {
      summary = Observation(operation, failed.Kind, failed.Elapsed, failed.Terminated, failed.ExitCode, false, failed.Suppressed);
      failed = failed with { ReportedAt = _time.GetTimestamp(), Suppressed = 0 };
    }
    _failures[operation] = failed;
    return summary;
  }

  // First rejection of each operation is reported at once, then at most every 60 seconds with the count
  // merged since the last record. This call started nothing, so it reports a confirmed termination.
  private WmiProbeObservation? RecordRejection(string operation, string pendingEvidence)
  {
    if (_rejections.TryGetValue(operation, out var previous) &&
        _time.GetElapsedTime(previous.ReportedAt) < TimeSpan.FromSeconds(60))
    {
      _rejections[operation] = previous with { Suppressed = previous.Suppressed + 1 };
      return null;
    }
    _rejections[operation] = new(_time.GetTimestamp(), 0);
    return new(operation, "cleanup-pending", 0, (long)Budget.TotalMilliseconds, true, null, previous?.Suppressed ?? 0, false,
      CleanupEvidence: "not-started", PendingCleanupEvidence: pendingEvidence);
  }

  private static bool ValidReply(string operation, string[] fields)
  {
    if (fields.Length < 3 || fields[0] != "SGWMI1") return false;
    if (operation == "brightness-write") return fields.Length == 3 && fields[1] == "write" && fields[2] == "ok";
    if (!int.TryParse(fields[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) || value is < 0 or > 100) return false;
    return operation == "brightness-read"
      ? fields.Length == 3 && fields[1] == "brightness"
      : fields.Length == 4 && fields[1] == "battery" && fields[3] is "ac" or "battery" or "unknown";
  }

  private static string SafeChildError(PowerCfgCommandResult result)
  {
    if (result.ExitCode != 3 || result.StandardError.Length != 0 || result.StandardOutput.Length > 48) return "child-failed";
    var fields = result.StandardOutput.TrimEnd('\r', '\n').Split('|');
    return fields.Length == 3 && fields[0] == "SGWMI1" && fields[1] == "error" &&
      fields[2] is "unavailable" or "wmi-failed" or "provider-failed" or "write-failed"
      ? fields[2] : "child-failed";
  }

  internal void ReportRestoreDegraded(string failureKind, long elapsedMilliseconds, long budgetMilliseconds)
  {
    bool terminated;
    lock (_sync) terminated = _pending is null && !_busy;
    Publish(new("brightness-restore", failureKind, elapsedMilliseconds, budgetMilliseconds, terminated, null, 0, false));
  }

  private void Fail(string operation, string kind, long elapsed, bool terminated, int? exitCode,
    string? cleanupEvidence = null, UnconfirmedChild? pendingCleanup = null)
  {
    WmiProbeObservation? failure = null;
    lock (_sync)
    {
      if (!terminated && _pending is null)
      {
        _pending = pendingCleanup ?? UnconfirmedChild.Unprovable();
        _pendingSince = _time.GetTimestamp();
      }
      var report = !_failures.TryGetValue(operation, out var previous) || previous.Kind != kind ||
        _time.GetElapsedTime(previous.ReportedAt) >= TimeSpan.FromSeconds(60);
      var suppressed = previous?.Suppressed ?? 0;
      if (!report) suppressed++; // Repeated actual failures are also coalesced observations.
      _failures[operation] = new(kind, report ? _time.GetTimestamp() : previous!.ReportedAt,
        _time.GetTimestamp(), elapsed, terminated, exitCode, report ? 0 : suppressed);
      if (report) failure = Observation(operation, kind, elapsed, terminated, exitCode, false, suppressed, cleanupEvidence);
    }
    Publish(failure);
  }

  private WmiProbeObservation Observation(string operation, string kind, long elapsed, bool terminated, int? exitCode, bool recovered,
    long suppressed, string? cleanupEvidence = null)
    => new(operation, kind, elapsed, (long)Budget.TotalMilliseconds, terminated, exitCode, suppressed, recovered,
      CleanupEvidence: cleanupEvidence);

  private void Publish(WmiProbeObservation? observation)
  {
    if (observation is null) return;
    // Never call an external observer while holding the process ownership lock.
    try { _observe?.Invoke(observation); }
    catch { /* Telemetry failure cannot change probe ownership or guardian progress. */ }
  }
}
