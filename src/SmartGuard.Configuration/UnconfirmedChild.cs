using System.Diagnostics;

namespace SmartGuard.Configuration;

/// <summary>Scaffold only; the proof arrives with its tests.</summary>
internal sealed class UnconfirmedChild
{
  private UnconfirmedChild(string evidence) => Evidence = evidence;

  internal string Evidence { get; private set; }

  internal static UnconfirmedChild FromProcess(Process process, Task<PipeReadOutcome> stdout, Task<PipeReadOutcome> stderr,
    CancellationTokenSource readerCts, string evidence) => new(evidence);

  internal static UnconfirmedChild FromExecution(Task<PowerCfgCommandResult> execution) => new("executor-pending");

  internal static UnconfirmedChild Unprovable() => new("unprovable");

  internal static UnconfirmedChild ForTests(Func<(bool Confirmed, string Evidence)> proof, string evidence) => new(evidence);

  internal bool TryConfirm() => false;
}
