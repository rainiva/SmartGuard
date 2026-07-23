namespace SmartGuard.Configuration;

public static class PauseGuardMessages
{
  public static string? GetLogMessage(bool? previousPaused, bool currentPaused)
  {
    if (previousPaused is null || previousPaused == currentPaused) return null;
    return currentPaused
      ? "暂停守护已开启（仅监控，不自动切换电源计划）"
      : "暂停守护已关闭（守护已恢复）";
  }
}
