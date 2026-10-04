using System.Security.Cryptography;
using System.Text;

namespace SmartGuard.Configuration;

public static partial class SmartGuardLogWriter
{
  // 统一日志追加入口：命名 Mutex（最佳努力）+ FileShare 追加 + 有限重试；永不抛出。
  public static bool TryAppendLine(string path, string line)
    => TryAppendLines(path, new[] { line }, retention: null);

  public static bool TryAppendLines(
    string path,
    IReadOnlyList<string> lines,
    LogRetentionPolicy? retention,
    DateTime? now = null)
  {
    if (lines.Count == 0)
      return true;

    try
    {
      var dir = Path.GetDirectoryName(path);
      if (!string.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir);

      if (retention is null)
        return AppendLinesBestEffort(path, lines);

      Mutex? mutex = null;
      var held = false;
      try
      {
        try
        {
          mutex = new Mutex(false, MutexName(path));
          held = mutex.WaitOne(TimeSpan.FromSeconds(2));
        }
        catch (AbandonedMutexException)
        {
          held = true;
        }
        catch
        {
          return false;
        }

        if (!held)
          return false;

        var timestamp = now ?? DateTime.Now;
        PrepareConfiguredLog(path, retention, timestamp);
        return AppendLinesInternal(path, lines);
      }
      finally
      {
        if (mutex is not null)
        {
          if (held)
          {
            try { mutex.ReleaseMutex(); } catch { /* 忽略释放失败 */ }
          }
          mutex.Dispose();
        }
      }
    }
    catch
    {
      return false;
    }
  }

  private static bool AppendLinesBestEffort(string path, IReadOnlyList<string> lines)
  {
    Mutex? mutex = null;
    var held = false;
    try
    {
      try
      {
        mutex = new Mutex(false, MutexName(path));
        held = mutex.WaitOne(TimeSpan.FromSeconds(2));
      }
      catch (AbandonedMutexException)
      {
        held = true;
      }
      catch
      {
        mutex = null;
      }

      return AppendLinesInternal(path, lines);
    }
    finally
    {
      if (mutex is not null)
      {
        if (held)
        {
          try { mutex.ReleaseMutex(); } catch { /* 忽略释放失败 */ }
        }
        mutex.Dispose();
      }
    }
  }

  private static bool AppendLinesInternal(string path, IReadOnlyList<string> lines)
  {
    for (var attempt = 0; attempt < 3; attempt++)
    {
      try
      {
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var sw = new StreamWriter(fs, new UTF8Encoding(false));
        foreach (var line in lines)
          sw.WriteLine(line);
        return true;
      }
      catch (IOException)
      {
        Thread.Sleep(20);
      }
    }

    return false;
  }

  private static void PrepareConfiguredLog(string logPath, LogRetentionPolicy retention, DateTime now)
  {
    if (File.Exists(logPath))
    {
      var fileDay = DateOnly.FromDateTime(File.GetLastWriteTime(logPath));
      var today = DateOnly.FromDateTime(now);
      if (LogArchivePlanner.ShouldRotateForCalendarDay(fileDay, today))
        ArchiveCurrentLog(logPath, fileDay);

      else if (new FileInfo(logPath).Length > retention.MaxBytes)
        ArchiveCurrentLog(logPath, today);
    }

    PruneExpiredArchives(logPath, retention.RetainDays, now);
    EnforceArchiveLimits(logPath, retention.MaxArchiveFiles, retention.MaxDirectoryBytes);
  }

  private static void ArchiveCurrentLog(string logPath, DateOnly archiveDate)
  {
    if (!File.Exists(logPath))
      return;

    var archivePath = LogArchivePlanner.AllocateArchivePath(logPath, archiveDate);
    var dir = Path.GetDirectoryName(archivePath);
    if (!string.IsNullOrEmpty(dir))
      Directory.CreateDirectory(dir);
    File.Move(logPath, archivePath);
  }

  private static void PruneExpiredArchives(string logPath, int retainDays, DateTime now)
  {
    var dir = Path.GetDirectoryName(logPath);
    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
      return;

    var prefix = Path.GetFileName(logPath) + ".";
    var today = DateOnly.FromDateTime(now);
    foreach (var path in Directory.EnumerateFiles(dir, prefix + "*.bak"))
    {
      if (!LogArchivePlanner.TryParseArchiveDate(path, out var archiveDate))
        continue;
      if (!LogArchivePlanner.IsArchiveExpired(archiveDate, today, retainDays))
        continue;
      File.Delete(path);
    }
  }

  private static void EnforceArchiveLimits(string logPath, int maxArchiveFiles, long maxDirectoryBytes)
  {
    var dir = Path.GetDirectoryName(logPath);
    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
      return;

    var prefix = Path.GetFileName(logPath) + ".";
    var archives = new List<(string Path, DateOnly Date, long SizeBytes)>();
    foreach (var path in Directory.EnumerateFiles(dir, prefix + "*.bak"))
    {
      if (!LogArchivePlanner.TryParseArchiveDate(path, out var archiveDate))
        continue;
      archives.Add((path, archiveDate, new FileInfo(path).Length));
    }

    foreach (var doomed in LogArchivePlanner.SelectArchivesBeyondCount(
               archives.Select(a => (a.Path, a.Date)).ToList(),
               maxArchiveFiles))
    {
      File.Delete(doomed);
      archives.RemoveAll(a => a.Path == doomed);
    }

    var currentBytes = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;
    foreach (var doomed in LogArchivePlanner.SelectArchivesOverBudget(
               archives,
               currentBytes,
               maxDirectoryBytes))
      File.Delete(doomed);
  }

  internal static string MutexNameForTests(string path) => MutexName(path);

  private static string MutexName(string path)
  {
    var normalized = Path.GetFullPath(path).ToLowerInvariant();
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    return @"Local\SmartGuardLog_" + hash[..16];
  }
}
