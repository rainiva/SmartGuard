using System.Globalization;
using System.Text.RegularExpressions;
using SmartGuard.LogViewer;

namespace SmartGuard.Settings;

public static partial class LogLineTimestampParser
{
    private static readonly Regex TimestampPrefix = TimestampPrefixPattern();

    public static bool TryParseTimestamp(string line, out DateTime timestamp)
    {
        timestamp = default;
        if (!LogLineTagParser.TryParse(line, out _, out var body))
            return false;

        var match = TimestampPrefix.Match(body);
        if (!match.Success)
            return false;

        return DateTime.TryParseExact(
            match.Groups["timestamp"].Value,
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out timestamp);
    }

    [GeneratedRegex(@"^(?<timestamp>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})\b", RegexOptions.Compiled)]
    private static partial Regex TimestampPrefixPattern();
}
