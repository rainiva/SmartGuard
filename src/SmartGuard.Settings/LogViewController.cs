using SmartGuard.LogViewer;

namespace SmartGuard.Settings;

public sealed class LogViewController
{
    private const int DefaultMaxTailBytes = 262_144;

    private readonly string _logPath;
    private readonly string? _fallbackLogPath;
    private long _fileLength = -1;
    private string _cachedText = string.Empty;
    private bool _isTailTruncated;
    private bool _contentChanged;

    public string SearchKeyword { get; set; } = string.Empty;
    public bool SearchCaseSensitive { get; set; }
    public bool ShowInfo { get; set; } = true;
    public bool ShowWarn { get; set; } = true;
    public bool ShowError { get; set; } = true;
    public bool ShowHeart { get; set; } = true;
    public bool FollowTail { get; set; } = true;
    public LogViewTimeRange TimeRange { get; set; } = LogViewTimeRange.All;
    public DateTime? CustomRangeStart { get; set; }
    public DateTime? CustomRangeEnd { get; set; }
    public Func<DateTime>? NowProvider { get; set; }

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
        _isTailTruncated = _fileLength > DefaultMaxTailBytes;
        _contentChanged = true;
    }

    public void RefreshFromDisk()
    {
        _contentChanged = false;

        var snapshot = LogTailReader.ReadFromOffset(_logPath, 0);
        if (snapshot.Length <= 0 && string.IsNullOrEmpty(snapshot.Text))
        {
            if (!string.IsNullOrEmpty(_cachedText))
            {
                _cachedText = string.Empty;
                _fileLength = 0;
                _isTailTruncated = false;
                _contentChanged = true;
            }
            else if (_fileLength < 0)
            {
                _fileLength = 0;
            }

            return;
        }

        if (_fileLength < 0 || snapshot.Length < _fileLength)
        {
            ForceRefresh();
            return;
        }

        if (snapshot.Length > _fileLength)
        {
            var delta = LogTailReader.ReadFromOffset(_logPath, _fileLength);
            var formattedDelta = LogLineDisplayFormatter.FormatText(
                LogViewerDelta.PrepareForAppend(_cachedText, delta.Text));
            _cachedText += formattedDelta;
            _fileLength = snapshot.Length;
            _isTailTruncated = _fileLength > DefaultMaxTailBytes;
            _contentChanged = true;
        }
    }

    public LogViewSnapshot GetSnapshot()
    {
        var filtered = GetFilteredLines();
        var emptyStateMessage = ResolveEmptyStateMessage(filtered);
        return new LogViewSnapshot(
            filtered,
            GetTotalLineCount(),
            _isTailTruncated,
            _logPath,
            _contentChanged,
            SearchKeyword,
            emptyStateMessage,
            TimeRange);
    }

    private string? ResolveEmptyStateMessage(IReadOnlyList<string> filtered)
    {
        if (!HasVisibleLevelFilter())
            return "请至少选择一种日志级别";

        if (filtered.Count == 0 && (HasActiveTimeFilter() || !string.IsNullOrWhiteSpace(SearchKeyword)))
            return "无匹配结果";

        return null;
    }

    private bool HasVisibleLevelFilter()
    {
        return ShowInfo || ShowWarn || ShowError || ShowHeart;
    }

    private bool HasActiveTimeFilter()
    {
        return TimeRange != LogViewTimeRange.All;
    }

    public IReadOnlyList<string> GetFilteredLines()
    {
        if (string.IsNullOrEmpty(_cachedText))
            return Array.Empty<string>();

        var newline = _cachedText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = _cachedText.Split(newline, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        var searchComparison = SearchCaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        var now = NowProvider?.Invoke() ?? DateTime.Now;

        foreach (var line in lines)
        {
            if (!LogLineTagParser.TryParse(line, out var tag, out _))
            {
                if (HasActiveTimeFilter())
                    continue;

                var rawLine = $"[RAW] {line}";
                if (!string.IsNullOrEmpty(SearchKeyword) &&
                    !rawLine.Contains(SearchKeyword, searchComparison))
                    continue;

                result.Add(rawLine);
                continue;
            }

            if (!IsTagVisible(tag))
                continue;

            if (!string.IsNullOrEmpty(SearchKeyword) &&
                !line.Contains(SearchKeyword, searchComparison))
                continue;

            if (HasActiveTimeFilter())
            {
                if (!LogLineTimestampParser.TryParseTimestamp(line, out var timestamp))
                    continue;

                if (!LogViewTimeRangeFilter.IsWithinRange(TimeRange, timestamp, now, CustomRangeStart, CustomRangeEnd))
                    continue;
            }

            result.Add(line);
        }

        return result;
    }

    private int GetTotalLineCount()
    {
        if (string.IsNullOrEmpty(_cachedText))
            return 0;

        var newline = _cachedText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return _cachedText.Split(newline, StringSplitOptions.RemoveEmptyEntries).Length;
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
