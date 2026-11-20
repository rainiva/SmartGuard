namespace SmartGuard.Configuration;

public static class LogArchivePlanner
{
  public const int DefaultRetainDays = 7;

  public static bool ShouldRotateForCalendarDay(DateOnly fileDay, DateOnly today)
    => fileDay < today;

  public static bool IsArchiveExpired(DateOnly archiveDate, DateOnly today, int retainDays)
    => archiveDate < today.AddDays(-(retainDays - 1));

  public static string AllocateArchivePath(string logPath, DateOnly date)
  {
    var maxSequence = 0;
    var directory = Path.GetDirectoryName(logPath);
    if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
    {
      var prefix = Path.GetFileName(logPath) + $".{date:yyyyMMdd}";
      foreach (var path in Directory.EnumerateFiles(directory, prefix + "*.bak"))
      {
        if (TryParseArchiveIdentity(path, out _, out var sequence))
          maxSequence = Math.Max(maxSequence, sequence);
      }
    }

    return $"{logPath}.{date:yyyyMMdd}.{(maxSequence + 1):D3}.bak";
  }

  public static bool TryParseArchiveDate(string archivePath, out DateOnly date)
    => TryParseArchiveIdentity(archivePath, out date, out _);

  public static bool TryParseArchiveIdentity(string archivePath, out DateOnly date, out int sequence)
  {
    date = default;
    sequence = 0;
    var fileName = Path.GetFileName(archivePath);
    const string suffix = ".bak";
    if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
      return false;

    var withoutSuffix = fileName[..^suffix.Length];
    var dot = withoutSuffix.LastIndexOf('.');
    if (dot < 0 || dot == withoutSuffix.Length - 1)
      return false;

    var token = withoutSuffix[(dot + 1)..];
    if (token.Length == 8 && token.All(char.IsDigit))
    {
      if (!DateOnly.TryParseExact(token, "yyyyMMdd", out date))
        return false;
      return true;
    }

    if (token.Length == 3 && token.All(char.IsDigit))
    {
      var previousDot = withoutSuffix.LastIndexOf('.', dot - 1);
      if (previousDot < 0 || previousDot == withoutSuffix.Length - 1)
        return false;
      var dateToken = withoutSuffix[(previousDot + 1)..dot];
      if (dateToken.Length != 8 || !dateToken.All(char.IsDigit))
        return false;
      if (!DateOnly.TryParseExact(dateToken, "yyyyMMdd", out date))
        return false;
      sequence = int.Parse(token);
      return true;
    }

    return false;
  }

  public static IReadOnlyList<string> SelectArchivesBeyondCount(
    IReadOnlyList<(string Path, DateOnly Date)> archives,
    int maxArchiveFiles)
  {
    if (maxArchiveFiles <= 0 || archives.Count <= maxArchiveFiles)
      return Array.Empty<string>();

    return archives
      .OrderBy(a => a.Date)
      .ThenBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
      .Take(archives.Count - maxArchiveFiles)
      .Select(a => a.Path)
      .ToList();
  }

  public static IReadOnlyList<string> SelectArchivesOverBudget(
    IReadOnlyList<(string Path, DateOnly Date, long SizeBytes)> archives,
    long currentLogSizeBytes,
    long maxDirectoryBytes)
  {
    if (maxDirectoryBytes <= 0)
      return Array.Empty<string>();

    var total = currentLogSizeBytes + archives.Sum(a => a.SizeBytes);
    if (total <= maxDirectoryBytes)
      return Array.Empty<string>();

    var doomed = new List<string>();
    foreach (var archive in archives.OrderBy(a => a.Date).ThenBy(a => a.Path, StringComparer.OrdinalIgnoreCase))
    {
      if (total <= maxDirectoryBytes)
        break;
      doomed.Add(archive.Path);
      total -= archive.SizeBytes;
    }

    return doomed;
  }
}
