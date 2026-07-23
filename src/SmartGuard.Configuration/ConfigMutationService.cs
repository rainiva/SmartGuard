namespace SmartGuard.Configuration;

public sealed class ConfigMutationService(GuardConfigRepository repository)
{
  public void SetPaused(bool paused, string root, string fallbackLogPath)
  {
    var config = repository.LoadOrDefault(root);
    if (config.Paused == paused)
      return;

    var previousPaused = config.Paused;
    config.Paused = paused;
    var pauseMsg = PauseGuardMessages.GetLogMessage(previousPaused, paused);
    if (pauseMsg is not null)
      repository.AppendInfoLog(pauseMsg, root, fallbackLogPath);
    repository.Save(config);
  }

  public void SetManualHighPerformanceUntil(DateTime until)
    => repository.SetManualHighPerformanceUntil(until);
}
