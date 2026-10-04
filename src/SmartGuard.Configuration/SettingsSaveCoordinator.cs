namespace SmartGuard.Configuration;

public static class SettingsSaveCoordinator
{
  internal static Action<bool, string>? AutoStartOverrideForTests { get; set; }

  public static void Save(GuardConfig newConfig, GuardConfig previous, string root, GuardConfigRepository repository)
  {
    var pauseMsg = PauseGuardMessages.GetLogMessage(previous.Paused, newConfig.Paused);
    try
    {
      repository.Save(newConfig);
    }
    catch
    {
      repository.AppendMutationFailureLog(root, SmartGuardPaths.StartupLogFile(root));
      throw;
    }
    if (pauseMsg is not null)
      repository.AppendInfoLog(pauseMsg + "，已保存", root, SmartGuardPaths.StartupLogFile(root));

    var oldPaths = GuardConfigValidator.NormalizeProgramPaths(previous.ForegroundProgramPaths);
    var newPaths = GuardConfigValidator.NormalizeProgramPaths(newConfig.ForegroundProgramPaths);
    if (previous.ForegroundRestrictionEnabled != newConfig.ForegroundRestrictionEnabled
      || !oldPaths.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(newPaths))
      repository.AppendInfoLog($"程序规则已保存：限制{(newConfig.ForegroundRestrictionEnabled ? "开启" : "关闭")}；名单 {newPaths.Count} 项",
        root, SmartGuardPaths.StartupLogFile(root));

    if (AutoStartService.NeedsUpdate(newConfig.AutoStartEnabled, previous.AutoStartEnabled))
    {
      // Configuration is already committed. A later system-task failure must
      // not be diagnosed as a rollback or an unpublished configuration.
      try { (AutoStartOverrideForTests ?? AutoStartService.SetEnabled)(newConfig.AutoStartEnabled, root); }
      catch (Exception ex) { throw new InvalidOperationException("配置已保存，但开机自启同步失败", ex); }
    }
  }
}
