namespace SmartGuard.Configuration;

public sealed class ConfigMutationService(GuardConfigRepository repository)
{
  public void SetPaused(bool paused, string root, string fallbackLogPath)
  {
    var config = repository.LoadOrDefault(root);
    if (config.Paused == paused)
      return;

    // Repository reads may be cached. Mutating that object would expose an
    // unpublished policy even when the atomic file commit fails.
    var updated = GuardConfig.LoadFromJson(System.Text.Json.JsonSerializer.Serialize(config));
    var previousPaused = config.Paused;
    updated.Paused = paused;
    var pauseMsg = PauseGuardMessages.GetLogMessage(previousPaused, paused);
    try
    {
      repository.Save(updated);
    }
    catch
    {
      repository.AppendMutationFailureLog(root, fallbackLogPath);
      throw;
    }
    if (pauseMsg is not null)
      repository.AppendInfoLog(pauseMsg + "，已保存", root, fallbackLogPath);
  }

  public void SetManualHighPerformanceUntil(DateTime until)
    => repository.SetManualHighPerformanceUntil(until);
}
