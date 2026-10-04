namespace SmartGuard.Configuration;

public static class CrashLogWriter
{
  public static string CrashLogFile =>
    Path.Combine(Path.GetTempPath(), "SmartGuard", "crash.log");

  internal static string? CrashLogFileOverrideForTests;

  public static void Write(string component, string? mainLogPath, Exception ex, LogRetentionPolicy? mainRetention = null)
  {
    try
    {
      var line = $"[CRASH][{component}] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {ExceptionFormatter.Format(ex)}";
      // Unknown main budgets cannot authorize growth. Neither crash sink waits
      // for a mutex, rotates history or retries a failed write.
      if (!string.IsNullOrWhiteSpace(mainLogPath) && mainRetention is not null)
        SmartGuardLogWriter.TryAppendCrashLine(mainLogPath!, line, mainRetention);
      SmartGuardLogWriter.TryAppendSupplementalLines(EffectiveCrashLogFile, [line], emergency: true);
    }
    catch
    {
      // 崩溃处理路径本身绝不再抛出
    }
  }

  private static string EffectiveCrashLogFile => CrashLogFileOverrideForTests ?? CrashLogFile;
}
