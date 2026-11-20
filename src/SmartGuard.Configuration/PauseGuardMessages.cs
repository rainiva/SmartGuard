namespace SmartGuard.Configuration;

public static class PauseGuardMessages
{
  public static string? GetLogMessage(bool? previousPaused, bool currentPaused)
  {
    if (previousPaused is null || previousPaused == currentPaused) return null;
    return currentPaused
      ? "暂停守护选项已开启"
      : "暂停守护选项已关闭";
  }
}
