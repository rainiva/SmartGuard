namespace SmartGuard.Configuration;

public static class ManualHighPerformanceMessages
{
  public static string FormatApplied(DateTime until)
    => $"托盘手动切换高性能（保持至 {until:yyyy-MM-dd HH:mm}）";

  public static string FormatActivationDeferred(string failure)
    => $"托盘手动切换高性能：立即切换未完成（{failure}），守护程序将在下一轮切换";
}
