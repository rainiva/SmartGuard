namespace SmartGuard.Configuration;

public sealed class LogSourceCatalog
{
  public LogSourceCatalog(
    string configuredLogPath,
    IReadOnlyList<LogSourceDescriptor> mainHistoryOldestFirst,
    LogSourceDescriptor? startupFallback)
  {
    ConfiguredLogPath = configuredLogPath;
    MainHistoryOldestFirst = mainHistoryOldestFirst;
    MainHistoryNewestFirst = mainHistoryOldestFirst.Reverse().ToArray();
    StartupFallback = startupFallback;
  }

  public string ConfiguredLogPath { get; }

  public IReadOnlyList<LogSourceDescriptor> MainHistoryOldestFirst { get; }

  public IReadOnlyList<LogSourceDescriptor> MainHistoryNewestFirst { get; }

  public LogSourceDescriptor? StartupFallback { get; }

  public static LogSourceCatalog Build(string configuredLogPath, string? startupFallbackPath)
  {
    var newestFirstPaths = EnumerateMainHistoryPathsNewestFirst(configuredLogPath);
    var oldestFirst = new List<LogSourceDescriptor>(newestFirstPaths.Count);
    for (var i = newestFirstPaths.Count - 1; i >= 0; i--)
    {
      var path = newestFirstPaths[i];
      var kind = string.Equals(path, configuredLogPath, StringComparison.OrdinalIgnoreCase)
        ? LogSourceKind.Current
        : LogSourceKind.Archive;
      oldestFirst.Add(new LogSourceDescriptor(path, LogSourceIdentity.FromFilePath(path), kind));
    }

    LogSourceDescriptor? startup = null;
    if (!string.IsNullOrWhiteSpace(startupFallbackPath) && File.Exists(startupFallbackPath))
    {
      startup = new LogSourceDescriptor(
        startupFallbackPath,
        LogSourceIdentity.FromFilePath(startupFallbackPath),
        LogSourceKind.StartupFallback);
    }

    return new LogSourceCatalog(configuredLogPath, oldestFirst, startup);
  }

  internal static IReadOnlyList<string> EnumerateMainHistoryPathsNewestFirst(string logPath)
  {
    var sources = new List<string>();
    if (File.Exists(logPath))
      sources.Add(logPath);

    try
    {
      var directory = Path.GetDirectoryName(logPath);
      var fileName = Path.GetFileName(logPath);
      if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
      {
        sources.AddRange(Directory
          .EnumerateFiles(directory, fileName + ".*.bak", SearchOption.TopDirectoryOnly)
          .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase));
      }
    }
    catch
    {
      // 归档枚举失败不应阻断当前日志读取。
    }

    return sources;
  }
}
