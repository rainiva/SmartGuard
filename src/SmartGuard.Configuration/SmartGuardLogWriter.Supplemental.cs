using System.Text;

namespace SmartGuard.Configuration;

public sealed record SupplementalLogMaintenanceResult(bool Succeeded, bool Completed, int ExaminedFiles);

public static partial class SmartGuardLogWriter
{
  /// <summary>Bounded ordinary maintenance; never called by a crash callback.</summary>
  public static SupplementalLogMaintenanceResult MaintainSupplementalLog(string path, DateTime? now = null)
  {
    var examined = 0;
    var completed = false;
    var succeeded = WithExclusiveLog(path, TimeSpan.FromSeconds(2), () =>
    {
      if (File.Exists(path) && new FileInfo(path).Length > SupplementalLogPolicy.Retention.MaxBytes
          && !ReplaceSupplemental(path, ReadBoundedTail(path, (int)SupplementalLogPolicy.Retention.MaxBytes - 4096))) return false;
      var at = now ?? DateTime.Now;
      if (File.Exists(path) && File.GetLastWriteTime(path).Date < at.Date
          && !RotateSupplemental(path, File.GetLastWriteTime(path))) return false;
      completed = PruneSupplementalArchives(path, at, out examined);
      return true;
    });
    return new(succeeded, succeeded && completed, examined);
  }

  internal static bool TryAppendCrashLine(string path, string line, LogRetentionPolicy retention)
  {
    var batch = SupplementalLogPolicy.SelectBatch([line]).Lines;
    return WithExclusiveLog(path, TimeSpan.Zero, () =>
    {
      var bytes = batch.Sum(item => Encoding.UTF8.GetByteCount(item) + Environment.NewLine.Length);
      var current = File.Exists(path) ? new FileInfo(path).Length : 0;
      return retention.MaxBytes > 0 && current + bytes <= retention.MaxBytes && AppendOnce(path, batch);
    });
  }

  /// <summary>
  /// Supplemental append shares the canonical encoding/lock owner. Emergency
  /// mode never waits, retries, reads history or scans archives; OS IO itself is
  /// synchronous and is not claimed to have a wall-clock timeout.
  /// </summary>
  public static bool TryAppendSupplementalLines(string path, IReadOnlyList<string> lines,
    bool emergency = false, DateTime? now = null)
  {
    if (lines.Count == 0) return true;
    var batch = SupplementalLogPolicy.SelectBatch(lines).Lines;
    return WithExclusiveLog(path, emergency ? TimeSpan.Zero : TimeSpan.FromSeconds(2), () =>
    {
      var bytes = batch.Sum(line => Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length);
      var maxBytes = SupplementalLogPolicy.Retention.MaxBytes;
      var currentBytes = File.Exists(path) ? new FileInfo(path).Length : 0;
      if (emergency)
      {
        if (currentBytes + bytes > maxBytes)
          return ReplaceSupplemental(path, new[] { SupplementalLogPolicy.TruncationMarker("附加日志容量已截断，仅保留最新批次") }.Concat(batch));
        return AppendOnce(path, batch);
      }
      // Daily preparation/maintenance is consumer/startup work only.
      if (currentBytes > maxBytes)
      {
        if (!ReplaceSupplemental(path, ReadBoundedTail(path, (int)(maxBytes - bytes - 4096)))) return false;
        currentBytes = new FileInfo(path).Length;
      }
      var at = now ?? DateTime.Now;
      if (currentBytes > 0 && (currentBytes + bytes > maxBytes || File.GetLastWriteTime(path).Date < at.Date)
          && !RotateSupplemental(path, File.GetLastWriteTime(path))) return false;
      if (!AppendOnce(path, batch)) return false;
      PruneSupplementalArchives(path, at, out _);
      return true;
    });
  }

  private static bool WithExclusiveLog(string path, TimeSpan wait, Func<bool> operation)
  {
    Mutex? mutex = null;
    var held = false;
    try
    {
      mutex = new Mutex(false, MutexName(path));
      try { held = mutex.WaitOne(wait); }
      catch (AbandonedMutexException) { held = true; }
      if (!held) return false;
      var directory = Path.GetDirectoryName(path);
      if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
      return operation();
    }
    catch { return false; }
    finally
    {
      if (held) { try { mutex!.ReleaseMutex(); } catch { } }
      mutex?.Dispose();
    }
  }

  private static bool AppendOnce(string path, IReadOnlyList<string> lines)
  {
    using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
    using var writer = new StreamWriter(stream, new UTF8Encoding(false));
    foreach (var line in lines) writer.WriteLine(line);
    return true;
  }

  private static bool ReplaceSupplemental(string path, IEnumerable<string> lines)
  {
    var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
      using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
      using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        foreach (var line in lines) writer.WriteLine(line);
      // Same-directory replacement preserves the old file on publication failure.
      if (File.Exists(path)) File.Replace(temporary, path, null);
      else File.Move(temporary, path);
      return true;
    }
    catch { return false; }
    finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
  }

  private static IEnumerable<string> ReadBoundedTail(string path, int maxBytes)
  {
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    var count = (int)Math.Min(stream.Length, Math.Max(0, maxBytes));
    var start = stream.Length - count;
    stream.Seek(start, SeekOrigin.Begin);
    var bytes = new byte[count];
    stream.ReadExactly(bytes);
    var first = start == 0 ? 0 : Array.IndexOf(bytes, (byte)'\n') + 1;
    if (start != 0 && first == 0) first = bytes.Length;
    yield return SupplementalLogPolicy.TruncationMarker("旧附加日志超限，已截断为有界尾部");
    if (first < bytes.Length)
    {
      var last = Array.LastIndexOf(bytes, (byte)'\n');
      if (last < first) yield break;
      var text = new UTF8Encoding(false, true).GetString(bytes, first, last + 1 - first);
      foreach (var line in text.Split('\n')) if (line.Length > 0) yield return line.TrimEnd('\r');
    }
  }

  private static bool RotateSupplemental(string path, DateTime now)
  {
    // Maintenance retains at most three archives. Four bounded name probes
    // suffice in the ordinary state; legacy overflow fails closed until pruned.
    for (var sequence = 1; sequence <= 4; sequence++)
    {
      var archive = $"{path}.{now:yyyyMMdd}.{sequence:D3}.bak";
      if (File.Exists(archive)) continue;
      File.Move(path, archive);
      return true;
    }
    return false;
  }

  private static bool PruneSupplementalArchives(string path, DateTime now, out int examined)
  {
    examined = 0;
    var directory = Path.GetDirectoryName(path);
    if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return true;
    var prefix = Path.GetFileName(path) + ".";
    var candidates = Directory.EnumerateFiles(directory, prefix + "*.bak").Take(SupplementalLogPolicy.MaintenanceCandidateLimit).ToArray();
    examined = candidates.Length;
    var archives = new List<string>();
    foreach (var candidate in candidates)
    {
      if (!IsSupplementalArchive(path, candidate, out var date)) continue;
      if (LogArchivePlanner.IsArchiveExpired(date, DateOnly.FromDateTime(now), SupplementalLogPolicy.Retention.RetainDays)
          || new FileInfo(candidate).Length > SupplementalLogPolicy.Retention.MaxBytes)
      { File.Delete(candidate); continue; }
      archives.Add(candidate);
    }
    foreach (var doomed in archives.OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Skip(SupplementalLogPolicy.Retention.MaxArchiveFiles))
      File.Delete(doomed);
    // Conservatively incomplete at the cap; no 33rd candidate is inspected.
    return candidates.Length < SupplementalLogPolicy.MaintenanceCandidateLimit;
  }

  internal static bool IsSupplementalArchive(string activePath, string archivePath, out DateOnly date)
  {
    if (!LogArchivePlanner.TryParseArchiveIdentity(archivePath, out date, out var sequence)) return false;
    var expected = Path.GetFileName(activePath) + $".{date:yyyyMMdd}" + (sequence == 0 ? "" : $".{sequence:D3}") + ".bak";
    return string.Equals(Path.GetFileName(archivePath), expected, StringComparison.OrdinalIgnoreCase);
  }
}
