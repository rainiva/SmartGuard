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

  public bool Succeeded => Started && !TimedOut && ExitCode == 0 && FailureKind == "none" && TerminationConfirmed;

  public string CombinedOutput => StandardOutput + StandardError;
}
