namespace SmartGuard.Configuration;

public static class CrashLogWriter
{
  public static string CrashLogFile =>
    Path.Combine(Path.GetTempPath(), "SmartGuard", "crash.log");

  internal static string? CrashLogFileOverrideForTests;

  public static void Write(string component, string? mainLogPath, Exception ex)
  {
    try
    {
      var line = $"[CRASH][{component}] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {ExceptionFormatter.Format(ex)}";
      if (!string.IsNullOrWhiteSpace(mainLogPath))
        SmartGuardLogWriter.TryAppendLine(mainLogPath!, line); // 主日志尽力写
      SmartGuardLogWriter.TryAppendLine(EffectiveCrashLogFile, line); // 应急崩溃文件必写
    }
    catch
    {
      // 崩溃处理路径本身绝不再抛出
    }
  }

  private static string EffectiveCrashLogFile => CrashLogFileOverrideForTests ?? CrashLogFile;
}
