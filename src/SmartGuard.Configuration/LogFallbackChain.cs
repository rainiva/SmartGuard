namespace SmartGuard.Configuration;

public static class LogFallbackChain
{
  public static string EmergencyDirectory =>
    Path.Combine(Path.GetTempPath(), "SmartGuard");

  public static string EmergencyLogFile =>
    Path.Combine(EmergencyDirectory, "emergency.log");

  internal static string? EmergencyLogFileOverrideForTests;

  // 三层降级：主日志 → startup 回退 → %TEMP%\SmartGuard 应急；每层独立异常隔离，最终绝不上抛。
  public static bool Write(Func<bool> tryPrimary, string fallbackLogPath, string emergencyLine)
  {
    try { if (tryPrimary()) return true; } catch { /* 主层异常隔离，降级 */ }
    try { if (SmartGuardLogWriter.TryAppendLine(fallbackLogPath, "[LOG-FALLBACK] " + emergencyLine)) return true; }
    catch { /* 回退层异常隔离，降级 */ }
    try { return SmartGuardLogWriter.TryAppendLine(EffectiveEmergencyLogFile, "[LOG-EMERGENCY] " + emergencyLine); }
    catch { /* 应急层异常隔离，最终不再上抛 */ }
    return false;
  }

  public static bool WriteEvent(Func<bool> tryPrimary, string fallbackLogPath, LogEvent logEvent)
  {
    var emergencyLine = LogStructuredSink.FormatTextFallback(logEvent);
    return Write(tryPrimary, fallbackLogPath, emergencyLine);
  }

  /// <summary>Consumer-only batch fallback; preserve canonical identity and properties.</summary>
  public static LogBatchWriteResult WriteBatch(string fallbackLogPath,
    IReadOnlyList<LogEvent> events, bool primaryAttemptFailed)
  {
    try
    {
      if (LogStructuredSink.TryWriteBatch(fallbackLogPath, events, null))
        return new(0, events.Count, 0, primaryAttemptFailed);
    }
    catch { /* Try the last existing sink, once for the whole batch. */ }
    try
    {
      if (LogStructuredSink.TryWriteBatch(EffectiveEmergencyLogFile, events, null))
        return new(0, events.Count, 0, primaryAttemptFailed);
    }
    catch { /* No confirmed destination; the consumer reports the loss. */ }
    return new(0, 0, events.Count, primaryAttemptFailed);
  }

  private static string EffectiveEmergencyLogFile =>
    EmergencyLogFileOverrideForTests ?? EmergencyLogFile;
}
