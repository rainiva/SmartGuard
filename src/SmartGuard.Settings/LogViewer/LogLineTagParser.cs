using System.Text.RegularExpressions;

namespace SmartGuard.LogViewer;

public static partial class LogLineTagParser
{
  private static readonly Regex TagLinePattern = TagLine();

  public static bool TryParse(string line, out string tag, out string body)
  {
    tag = string.Empty;
    body = string.Empty;
    if (string.IsNullOrEmpty(line) || line.StartsWith("---", StringComparison.Ordinal))
      return false;

    var match = TagLinePattern.Match(line);
    if (!match.Success) return false;

    tag = match.Groups["tag"].Value;
    body = match.Groups["body"].Value;
    return true;
  }

  [GeneratedRegex(@"^\[(?<tag>\w+)\]\s+(?<body>.+)$", RegexOptions.Compiled)]
  private static partial Regex TagLine();
}
