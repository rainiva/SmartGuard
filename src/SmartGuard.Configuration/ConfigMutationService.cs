namespace SmartGuard.Configuration;

public sealed class ConfigMutationService(GuardConfigRepository repository)
{
  public void SetPaused(bool paused, string root, string fallbackLogPath)
  {
    var previous = repository.TryLoad();
    var config = repository.LoadOrDefault(root);
    if (config.Paused == paused)
      return;

    config.Paused = paused;
    var pauseMsg = PauseGuardMessages.GetLogMessage(previous?.Paused, paused);
    if (pauseMsg is not null)
      repository.AppendInfoLog(pauseMsg, fallbackLogPath);
    repository.Save(config);
  }

  public void SetManualHighPerformanceUntil(DateTime until)
    => repository.SetManualHighPerformanceUntil(until);
}
