namespace SmartGuard.Engine.Infrastructure;

public static class FileLogger
{
  [ThreadStatic]
  private static bool _writing;

  internal static Action? ReentrancyProbeForTests;

  public static void RotateDailyIfNeeded(string logPath, DateTime now)
  {
    if (!File.Exists(logPath)) return;
    var fileDay = DateOnly.FromDateTime(File.GetLastWriteTime(logPath));
    var today = DateOnly.FromDateTime(now);
    if (!LogArchivePlanner.ShouldRotateForCalendarDay(fileDay, today)) return;
    ArchiveCurrentLog(logPath, fileDay);
  }

  public static void RotateIfNeeded(string logPath, long maxBytes, DateTime now)
  {
    if (!File.Exists(logPath)) return;
    if (new FileInfo(logPath).Length <= maxBytes) return;
    ArchiveCurrentLog(logPath, DateOnly.FromDateTime(now));
  }

  public static void PruneExpiredArchives(string logPath, int retainDays, DateTime now)
  {
    var dir = Path.GetDirectoryName(logPath);
    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
    var prefix = Path.GetFileName(logPath) + ".";
    var today = DateOnly.FromDateTime(now);
    foreach (var path in Directory.EnumerateFiles(dir, prefix + "*.bak"))
    {
      if (!LogArchivePlanner.TryParseArchiveDate(path, out var archiveDate)) continue;
      if (!LogArchivePlanner.IsArchiveExpired(archiveDate, today, retainDays)) continue;
      File.Delete(path);
    }
  }

  public static void Write(LogLevel level, string logPath, string message, long maxBytes, DateTime? now = null)
  {
    if (_writing) return; // 防递归：日志内部再触发写入直接短路，避免栈溢出与雪崩
    _writing = true;
    try
    {
      ReentrancyProbeForTests?.Invoke();
      var timestamp = now ?? DateTime.Now;
      PrepareLogFile(logPath, maxBytes, timestamp);
      var line = LogLineFormatter.Format(timestamp, level, message);
      WriteLine(logPath, line);
    }
    catch
    {
      // 日志写入失败绝不上抛，避免拖垮 GuardianLoop 主循环；不在此处再次记日志（防递归）
    }
    finally
    {
      _writing = false;
    }
  }

  public static bool TryWrite(LogLevel level, string logPath, string message, long maxBytes, DateTime? now = null)
  {
    if (_writing) return false; // 复用 Slice 1 的防递归守卫
    _writing = true;
    try
    {
      var timestamp = now ?? DateTime.Now;
      PrepareLogFile(logPath, maxBytes, timestamp);
      WriteLine(logPath, LogLineFormatter.Format(timestamp, level, message));
      return true;
    }
    catch
    {
      return false; // 失败上报给降级链，绝不上抛
    }
    finally
    {
      _writing = false;
    }
  }

  public static bool TryWriteLines(string logPath, IReadOnlyList<string> lines, long maxBytes, DateTime? now = null)
  {
    if (lines.Count == 0) return true; // 空批直接成功，不碰磁盘不触发轮转
    if (_writing) return false; // 复用 [ThreadStatic] 防递归守卫
    _writing = true;
    try
    {
      var timestamp = now ?? DateTime.Now;
      PrepareLogFile(logPath, maxBytes, timestamp);
      var dir = Path.GetDirectoryName(logPath);
      if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
      using var fs = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
      using var sw = new StreamWriter(fs, System.Text.Encoding.UTF8);
      foreach (var line in lines)
        sw.WriteLine(line);
      return true;
    }
    catch
    {
      return false; // 批量失败上报给消费侧降级，绝不上抛
    }
    finally
    {
      _writing = false;
    }
  }

  public static void WriteLine(string logPath, string line)
  {
    var dir = Path.GetDirectoryName(logPath);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    using var fs = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
    using var sw = new StreamWriter(fs, System.Text.Encoding.UTF8);
    sw.WriteLine(line);
  }

  private static void PrepareLogFile(string logPath, long maxBytes, DateTime now)
  {
    RotateDailyIfNeeded(logPath, now);
    RotateIfNeeded(logPath, maxBytes, now);
    PruneExpiredArchives(logPath, LogArchivePlanner.DefaultRetainDays, now);
  }

  private static void ArchiveCurrentLog(string logPath, DateOnly archiveDate)
  {
    if (!File.Exists(logPath)) return;
    var archivePath = LogArchivePlanner.GetArchivePath(logPath, archiveDate);
    if (File.Exists(archivePath))
    {
      var existing = File.ReadAllText(archivePath);
      var incoming = File.ReadAllText(logPath);
      var merged = string.IsNullOrEmpty(existing) ? incoming : existing + Environment.NewLine + incoming;
      File.WriteAllText(archivePath, merged);
      File.Delete(logPath);
      return;
    }

    File.Move(logPath, archivePath);
  }
}
