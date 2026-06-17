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
}
