using System.Text.RegularExpressions;

namespace SmartGuard.LogViewer;

public static class LogViewerComposer
{
  private static readonly Regex TimestampPattern = new(
    @"\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}",
    RegexOptions.Compiled);

  public static string MergeChronologically(string? primary, string? fallback)
  {
    var entries = new List<LogDisplayEntry>();
    AppendSource(entries, primary, sourceOrder: 0);
    AppendSource(entries, fallback, sourceOrder: 1);

    if (entries.Count == 0) return string.Empty;
    if (entries.All(e => e.Timestamp is null))
    {
      return string.Join(Environment.NewLine, entries.Select(e => e.Text));
    }

    return string.Join(
      Environment.NewLine,
      entries
        .OrderBy(e => e.Timestamp ?? DateTime.MaxValue)
        .ThenBy(e => e.SourceOrder)
        .ThenBy(e => e.LineOrder)
        .Select(e => e.Text));
  }

  public static bool TryParseTimestamp(string line, out DateTime timestamp)
  {
    timestamp = default;
    var match = TimestampPattern.Match(line);
    if (!match.Success) return false;
    return DateTime.TryParseExact(
      match.Value,
      "yyyy-MM-dd HH:mm:ss",
      System.Globalization.CultureInfo.InvariantCulture,
      System.Globalization.DateTimeStyles.None,
      out timestamp);
  }

  private static void AppendSource(List<LogDisplayEntry> entries, string? text, int sourceOrder)
  {
    if (string.IsNullOrEmpty(text)) return;

    var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    var lines = text.Split(newline, StringSplitOptions.None);
    for (var i = 0; i < lines.Length; i++)
    {
      var line = lines[i];
      if (line.Length == 0 && i == lines.Length - 1) continue;
      DateTime? timestamp = TryParseTimestamp(line, out var parsed) ? parsed : null;
      entries.Add(new LogDisplayEntry(timestamp, sourceOrder, i, line));
    }
  }

  private sealed record LogDisplayEntry(DateTime? Timestamp, int SourceOrder, int LineOrder, string Text);
}
