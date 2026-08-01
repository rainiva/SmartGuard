namespace SmartGuard.Engine.Infrastructure;

public static class LogLevelFilter
{
  public static bool ShouldWrite(LogLevel level, LogLevel minimum)
  {
    if (level == LogLevel.Heart) return true; // Heart 不参与级别过滤，由 HeartLogSampler 采样治理
    return level >= minimum;
  }

  public static LogLevel ParseMinimum(string? text)
  {
    if (Enum.TryParse<LogLevel>(text, ignoreCase: true, out var parsed) && parsed != LogLevel.Heart)
      return parsed;
    return LogLevel.Info; // null/非法/Heart 一律回退 Info，保证配置损坏时行为与阶段一一致
  }
}
