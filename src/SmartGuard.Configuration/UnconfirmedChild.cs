using System.Diagnostics;

namespace SmartGuard.Configuration;

/// <summary>
/// Owns one child whose termination is not yet proven (PWR-INV-CLEANUP-REOPEN). Proof = the child exited
/// and both redirected pipes reached EOF. The proof never waits and never enumerates processes. Not
/// thread-safe: the holder serializes calls.
/// </summary>
internal sealed class UnconfirmedChild
{
  private Process? _process;
  private readonly Task<PipeReadOutcome>? _stdout;
  private readonly Task<PipeReadOutcome>? _stderr;
  private CancellationTokenSource? _readerCts;
  private readonly Task<PowerCfgCommandResult>? _execution;
  private readonly Func<(bool Confirmed, string Evidence)>? _proofForTests;
  private UnconfirmedChild? _delegate;
  private bool _confirmed;

  private UnconfirmedChild(string evidence, Process? process = null, Task<PipeReadOutcome>? stdout = null,
    Task<PipeReadOutcome>? stderr = null, CancellationTokenSource? readerCts = null,
    Task<PowerCfgCommandResult>? execution = null, Func<(bool, string)>? proofForTests = null)
  {
    Evidence = evidence;
    _process = process;
    _stdout = stdout;
    _stderr = stderr;
    _readerCts = readerCts;
    _execution = execution;
    _proofForTests = proofForTests;
  }

  /// <summary>kill-failed, not-exited, pipes-open, reader-failed, executor-pending, unprovable or confirmed.</summary>
  internal string Evidence { get; private set; }

  internal static UnconfirmedChild FromProcess(Process process, Task<PipeReadOutcome> stdout, Task<PipeReadOutcome> stderr,
    CancellationTokenSource readerCts, string evidence) => new(evidence, process, stdout, stderr, readerCts);

  // The executor did not return in time; its eventual result decides (a fault can never prove).
  internal static UnconfirmedChild FromExecution(Task<PowerCfgCommandResult> execution) => new("executor-pending", execution: execution);

  internal static UnconfirmedChild Unprovable() => new("unprovable");

  internal static UnconfirmedChild ForTests(Func<(bool Confirmed, string Evidence)> proof, string evidence)
    => new(evidence, proofForTests: proof);

  internal bool TryConfirm()
  {
    if (_confirmed) return true;
    try { _confirmed = Probe(); }
    catch { _confirmed = false; }
    if (!_confirmed) return false;
    Evidence = "confirmed";
    _process?.Dispose();
    _process = null;
    _readerCts?.Dispose();
    _readerCts = null;
    return true;
  }

  private bool Probe()
  {
    if (_proofForTests is { } proof)
    {
      var (confirmed, evidence) = proof();
      Evidence = evidence;
      return confirmed;
    }
    if (_execution is { } execution) return ProbeExecution(execution);
    if (_process is not { } process || _stdout is not { } stdout || _stderr is not { } stderr) return false;
    if (!process.HasExited)
    {
      // One TerminateProcess on the held handle, not a tree walk: constant cost.
      try { process.Kill(); Evidence = "not-exited"; }
      catch { Evidence = "kill-failed"; }
      return false;
    }
    if (stdout.IsFaulted || stdout.IsCanceled || stderr.IsFaulted || stderr.IsCanceled)
    {
      Evidence = "reader-failed";
      return false;
    }
    if (stdout.IsCompletedSuccessfully && stderr.IsCompletedSuccessfully && stdout.Result.ReachedEof && stderr.Result.ReachedEof)
      return true;
    Evidence = "pipes-open";
    return false;
  }

  private bool ProbeExecution(Task<PowerCfgCommandResult> execution)
  {
    if (_delegate is { } held)
    {
      var proven = held.TryConfirm();
      Evidence = held.Evidence;
      return proven;
    }
    if (!execution.IsCompleted) { Evidence = "executor-pending"; return false; }
    if (!execution.IsCompletedSuccessfully) { Evidence = "unprovable"; return false; }
    var result = execution.Result;
    if (result.TerminationConfirmed) return true;
    if (result.PendingCleanup is not { } child) { Evidence = "unprovable"; return false; }
    _delegate = child;
    var confirmed = child.TryConfirm();
    Evidence = child.Evidence;
    return confirmed;
  }
}
