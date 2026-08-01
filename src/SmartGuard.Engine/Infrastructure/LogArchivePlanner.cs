namespace SmartGuard.Engine.Infrastructure;

public static class LogArchivePlanner
{
  public const int DefaultRetainDays = 7;

  public static string GetArchivePath(string logPath, DateOnly date) =>
    $"{logPath}.{date:yyyyMMdd}.bak";

  public static bool ShouldRotateForCalendarDay(DateOnly fileDay, DateOnly today) =>
    fileDay < today;

  public static bool IsArchiveExpired(DateOnly archiveDate, DateOnly today, int retainDays) =>
    archiveDate < today.AddDays(-(retainDays - 1));

  public static bool TryParseArchiveDate(string archivePath, out DateOnly date)
  {
    date = default;
    var fileName = Path.GetFileName(archivePath);
    const string suffix = ".bak";
    if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
    var withoutSuffix = fileName[..^suffix.Length];
    var dot = withoutSuffix.LastIndexOf('.');
    if (dot < 0 || dot == withoutSuffix.Length - 1) return false;
    var token = withoutSuffix[(dot + 1)..];
    if (token.Length != 8 || !token.All(char.IsDigit)) return false;
    if (!DateOnly.TryParseExact(token, "yyyyMMdd", out date)) return false;
    return true;
  }

  public static IReadOnlyList<string> SelectArchivesBeyondCount(
    IReadOnlyList<(string Path, DateOnly Date)> archives, int maxArchiveFiles)
  {
    if (maxArchiveFiles <= 0 || archives.Count <= maxArchiveFiles) return Array.Empty<string>();
    return archives
      .OrderBy(a => a.Date)
      .Take(archives.Count - maxArchiveFiles)
      .Select(a => a.Path)
      .ToList();
  }

  public static IReadOnlyList<string> SelectArchivesOverBudget(
    IReadOnlyList<(string Path, DateOnly Date, long SizeBytes)> archives,
    long currentLogSizeBytes, long maxDirectoryBytes)
  {
    if (maxDirectoryBytes <= 0) return Array.Empty<string>();
    var total = currentLogSizeBytes + archives.Sum(a => a.SizeBytes);
    if (total <= maxDirectoryBytes) return Array.Empty<string>();
    var doomed = new List<string>();
    foreach (var archive in archives.OrderBy(a => a.Date))
    {
      if (total <= maxDirectoryBytes) break;
      doomed.Add(archive.Path);
      total -= archive.SizeBytes;
    }
    return doomed; // 只删归档、最旧先删；当前日志永不入选（即使删光归档仍超预算）
  }
}
