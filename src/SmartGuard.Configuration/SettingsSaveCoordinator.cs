namespace SmartGuard.Configuration;

public static class SettingsSaveCoordinator
{
  public static void Save(GuardConfig newConfig, GuardConfig previous, string root, GuardConfigRepository repository)
  {
    var pauseMsg = PauseGuardMessages.GetLogMessage(previous.Paused, newConfig.Paused);
    if (pauseMsg is not null)
    {
      var fallback = SmartGuardPaths.StartupLogFile(root);
      repository.AppendInfoLog(pauseMsg, fallback);
    }

    repository.Save(newConfig);

    if (AutoStartService.NeedsUpdate(newConfig.AutoStartEnabled, previous.AutoStartEnabled))
      AutoStartService.SetEnabled(newConfig.AutoStartEnabled, root);
  }
}
