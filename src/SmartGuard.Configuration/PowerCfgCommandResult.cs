namespace SmartGuard.Configuration;

public sealed record PowerCfgCommandResult(
  bool Started,
  bool TimedOut,
  int? ExitCode,
  string StandardOutput,
  string StandardError)
{
  public bool Succeeded => Started && !TimedOut && ExitCode == 0;

  public string CombinedOutput => StandardOutput + StandardError;
}
