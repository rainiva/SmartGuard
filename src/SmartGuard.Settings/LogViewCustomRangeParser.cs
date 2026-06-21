using System.Globalization;

namespace SmartGuard.Settings;

public static class LogViewCustomRangeParser
{
    private static readonly string[] Formats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd"];

    public static bool TryParse(string? text, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return DateTime.TryParseExact(
                   text.Trim(),
                   Formats,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out value)
               || DateTime.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }
}
