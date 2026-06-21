using System.Text.RegularExpressions;

namespace SmartGuard.Configuration;

public static partial class PowerPlanCatalogParser
{
  public static Dictionary<Guid, string> ParseList(string output)
  {
    var result = new Dictionary<Guid, string>();
    foreach (Match match in PowerSchemeListPattern().Matches(output))
    {
      var guid = Guid.Parse(match.Groups[1].Value);
      var name = match.Groups[2].Value.Trim();
      result[guid] = name;
    }

    return result;
  }

  public static bool TryParseQueryHeader(string output, out Guid guid, out string name)
  {
    guid = Guid.Empty;
    name = string.Empty;

    var match = PowerSchemeListPattern().Match(output);
    if (!match.Success)
      return false;

    guid = Guid.Parse(match.Groups[1].Value);
    name = match.Groups[2].Value.Trim();
    return true;
  }

  [GeneratedRegex(@"GUID:\s+([0-9a-fA-F-]{36})\s+\(([^)]+)\)", RegexOptions.IgnoreCase)]
  private static partial Regex PowerSchemeListPattern();
}
