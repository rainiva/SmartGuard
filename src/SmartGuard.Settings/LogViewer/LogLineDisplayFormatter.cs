using System.Text.RegularExpressions;

namespace SmartGuard.LogViewer;

public static class LogLineDisplayFormatter
{
  private static readonly Regex TagBeforeTimestamp = new(
    @"^\[(?<tag>\w+)\]\s+(?<timestamp>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})\s+(?<message>.*)$",
    RegexOptions.Compiled);

  private static readonly Regex TagAfterTimestamp = new(
    @"^(?<timestamp>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})\s+\[(?<tag>\w+)\]\s+(?<message>.*)$",
    RegexOptions.Compiled);

  private static readonly Regex LegacyDash = new(
    @"^(?<timestamp>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})\s+-\s+(?<message>.*)$",
    RegexOptions.Compiled);

  private static readonly Regex LegacyPlain = new(
    @"^(?<timestamp>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})\s+(?<message>.*)$",
    RegexOptions.Compiled);

  public static string FormatLine(string line)
  {
    if (string.IsNullOrEmpty(line)) return line;
    if (line.StartsWith("---", StringComparison.Ordinal)) return line;

    if (TagBeforeTimestamp.IsMatch(line)) return line;

    var afterTag = TagAfterTimestamp.Match(line);
    if (afterTag.Success)
    {
      return $"[{afterTag.Groups["tag"].Value}] {afterTag.Groups["timestamp"].Value} {afterTag.Groups["message"].Value}";
    }

    string timestamp;
    string message;
    var dash = LegacyDash.Match(line);
    if (dash.Success)
    {
      timestamp = dash.Groups["timestamp"].Value;
      message = dash.Groups["message"].Value;
    }
    else
    {
      var plain = LegacyPlain.Match(line);
      if (!plain.Success) return line;
      timestamp = plain.Groups["timestamp"].Value;
      message = plain.Groups["message"].Value;
    }

    var tag = InferTag(message);
    return $"[{tag}] {timestamp} {message}";
  }

  public static string FormatText(string text)
  {
    if (string.IsNullOrEmpty(text)) return text;

    var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    var lines = text.Split(newline, StringSplitOptions.None);
    for (var i = 0; i < lines.Length; i++)
      lines[i] = FormatLine(lines[i]);

    return string.Join(newline, lines);
  }

  private static string InferTag(string message)
  {
    if (message.StartsWith("EXTERNAL:", StringComparison.Ordinal)) return "WARN";
    if (message.StartsWith("ERROR:", StringComparison.Ordinal)) return "ERROR";
    if (message.StartsWith("WARN:", StringComparison.Ordinal)) return "WARN";
    if (message.StartsWith("[监控中]", StringComparison.Ordinal)) return "HEART";
    if (message.StartsWith("[LOG-FALLBACK]", StringComparison.Ordinal)) return "WARN";
    return "INFO";
  }
}
