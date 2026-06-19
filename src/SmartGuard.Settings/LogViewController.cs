using SmartGuard.LogViewer;

namespace SmartGuard.Settings;

public sealed class LogViewController
{
    private readonly string _logPath;
    private readonly string? _fallbackLogPath;
    private long _fileLength;
    private string _cachedText = string.Empty;

    public string SearchKeyword { get; set; } = string.Empty;
    public bool ShowInfo { get; set; } = true;
    public bool ShowWarn { get; set; } = true;
    public bool ShowError { get; set; } = true;
    public bool ShowHeart { get; set; } = true;

    public LogViewController(string logPath, string? fallbackLogPath = null)
    {
        _logPath = logPath;
        _fallbackLogPath = fallbackLogPath;
        ForceRefresh();
    }

    public void ForceRefresh()
    {
        var result = LogTailReader.ReadInitialView(_logPath, _fallbackLogPath);
        _fileLength = result.FileLength;
        _cachedText = LogLineDisplayFormatter.FormatText(result.Text);
    }

    public IReadOnlyList<string> GetFilteredLines()
    {
        if (string.IsNullOrEmpty(_cachedText))
            return Array.Empty<string>();

        var newline = _cachedText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = _cachedText.Split(newline, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();

        foreach (var line in lines)
        {
            if (!LogLineTagParser.TryParse(line, out var tag, out var body))
                continue;

            if (!IsTagVisible(tag))
                continue;

            if (!string.IsNullOrEmpty(SearchKeyword) &&
                !line.Contains(SearchKeyword, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(line);
        }

        return result;
    }

    private bool IsTagVisible(string tag)
    {
        return tag.ToUpperInvariant() switch
        {
            "INFO" => ShowInfo,
            "WARN" => ShowWarn,
            "ERROR" => ShowError,
            "HEART" => ShowHeart,
            "\u76D1\u63A7\u4E2D" => ShowHeart,
            _ => true,
        };
    }
}
