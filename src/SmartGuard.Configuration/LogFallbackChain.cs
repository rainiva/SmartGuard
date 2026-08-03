namespace SmartGuard.Configuration;

public static class LogFallbackChain
{
  public static string EmergencyDirectory =>
    Path.Combine(Path.GetTempPath(), "SmartGuard");

  public static string EmergencyLogFile =>
    Path.Combine(EmergencyDirectory, "emergency.log");

  internal static string? EmergencyLogFileOverrideForTests;

  // 三层降级：主日志 → startup 回退 → %TEMP%\SmartGuard 应急；每层独立异常隔离，最终绝不上抛。
  public static void Write(Func<bool> tryPrimary, string fallbackLogPath, string emergencyLine)
  {
    try { if (tryPrimary()) return; } catch { /* 主层异常隔离，降级 */ }
    try { if (SmartGuardLogWriter.TryAppendLine(fallbackLogPath, "[LOG-FALLBACK] " + emergencyLine)) return; }
    catch { /* 回退层异常隔离，降级 */ }
    try { SmartGuardLogWriter.TryAppendLine(EffectiveEmergencyLogFile, "[LOG-EMERGENCY] " + emergencyLine); }
    catch { /* 应急层异常隔离，最终不再上抛 */ }
  }

  public static void WriteEvent(Func<bool> tryPrimary, string fallbackLogPath, LogEvent logEvent)
  {
    var emergencyLine = LogStructuredSink.FormatTextFallback(logEvent);
    Write(tryPrimary, fallbackLogPath, emergencyLine);
  }

  private static string EffectiveEmergencyLogFile =>
    EmergencyLogFileOverrideForTests ?? EmergencyLogFile;
}
