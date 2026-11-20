namespace SmartGuard.Configuration;

public sealed record PowerCfgCommandResult(
  bool Started,
  bool TimedOut,
  int? ExitCode,
  string StandardOutput,
  string StandardError)
{
  public string FailureKind { get; init; } = "none";
  public bool TerminationConfirmed { get; init; } = true;
  public long ElapsedMilliseconds { get; init; } = 0;

  // Cleanup proof of this execution: confirmed, kill-failed, not-exited, pipes-open, reader-failed.
  public string? CleanupEvidence { get; init; }

  // Set only when the caller asked to retain an unproven child; the caller then owns it.
  internal UnconfirmedChild? PendingCleanup { get; init; }

  public bool Succeeded => Started && !TimedOut && ExitCode == 0 && FailureKind == "none" && TerminationConfirmed;

  public string CombinedOutput => StandardOutput + StandardError;
}
