namespace SmartGuard.Configuration;

public static class ManualHighPerformanceMessages
{
  public static string FormatApplied(DateTime until)
    => $"托盘手动切换高性能（保持至 {until:yyyy-MM-dd HH:mm}）";
}
