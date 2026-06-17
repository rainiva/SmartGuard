namespace SmartGuard.Configuration;

public static class PauseGuardMessages
{
  public static string? GetLogMessage(bool? previousPaused, bool currentPaused)
  {
    if (previousPaused is null || previousPaused == currentPaused) return null;
    return currentPaused ? "守护已暂停（仅监控，不切换计划）" : "守护已恢复";
  }
}
