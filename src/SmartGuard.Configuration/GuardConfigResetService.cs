namespace SmartGuard.Configuration;

public static class GuardConfigResetService
{
  public static GuardConfig CreateResetConfig(GuardConfig current, string root)
  {
    var reset = GuardConfig.CreateDefault(root);
    reset.LogFile = current.LogFile;
    reset.GitHubToken = current.GitHubToken;
    reset.ManualHighPerformanceUntil = null;
    return reset;
  }
}
