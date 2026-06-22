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
    private bool _isLoaded;
    private IReadOnlyList<string> _activeTagFilters = [];

    public string SearchKeyword { get; set; } = string.Empty;
    public bool SearchCaseSensitive { get; set; }
    public IReadOnlyList<string> ActiveTagFilters
    {
        get => _activeTagFilters;
        set => _activeTagFilters = value?.Where(tag => !string.IsNullOrWhiteSpace(tag)).ToArray() ?? [];
    }

    public bool FollowTail { get; set; } = true;
    public LogViewTimeRange TimeRange { get; set; } = LogViewTimeRange.All;
    public DateTime? CustomRangeStart { get; set; }
    public DateTime? CustomRangeEnd { get; set; }
    public Func<DateTime>? NowProvider { get; set; }

    internal int CachedTextLengthForTests => _cachedText.Length;

    public LogViewController(string logPath, string? fallbackLogPath = null)
    {
        _logPath = logPath;
        _fallbackLogPath = fallbackLogPath;
    }

    public void ForceRefresh()
    {
        var result = LogTailReader.ReadInitialView(_logPath, _fallbackLogPath);
        _fileLength = result.FileLength;
        _cachedText = LogLineDisplayFormatter.FormatText(result.Text);
        _isTailTruncated = _fileLength > DefaultMaxTailBytes;
        _contentChanged = true;
        _isLoaded = true;
        TrimCachedTextIfNeeded();
    }

    public void RefreshFromDisk()
    {
        EnsureLoaded();
        _contentChanged = false;

        var length = LogTailReader.GetFileLength(_logPath);
        if (length <= 0)
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

        if (_fileLength >= 0 && length == _fileLength)
            return;

        if (_fileLength < 0 || length < _fileLength)
        {
            ForceRefresh();
            return;
        }

        if (length > _fileLength)
        {
            var delta = LogTailReader.ReadFromOffset(_logPath, _fileLength);
            var formattedDelta = LogLineDisplayFormatter.FormatText(
                LogViewerDelta.PrepareForAppend(_cachedText, delta.Text));
            _cachedText += formattedDelta;
            _fileLength = length;
            _isTailTruncated = _fileLength > DefaultMaxTailBytes;
            _contentChanged = true;
            TrimCachedTextIfNeeded();
        }
    }

    public LogViewSnapshot GetSnapshot()
    {
        EnsureLoaded();
        var filtered = GetFilteredLines();
        var emptyStateMessage = ResolveEmptyStateMessage(filtered);
        var display = LogViewDisplaySlice.Select(
            filtered,
            LogViewDisplaySlice.DefaultMaxLines,
            preferTail: FollowTail);

        return new LogViewSnapshot(
            display.Lines,
            GetTotalLineCount(),
            _isTailTruncated,
            _logPath,
            _contentChanged,
            SearchKeyword,
            emptyStateMessage,
            TimeRange,
            filtered.Count,
            display.IsTruncated,
            _activeTagFilters);
    }

    private void EnsureLoaded()
    {
        if (_isLoaded)
            return;

        ForceRefresh();
    }

    private void TrimCachedTextIfNeeded()
    {
        _cachedText = LogViewCachedTextTrimmer.TrimToMaxBytes(_cachedText);
    }

    private string? ResolveEmptyStateMessage(IReadOnlyList<string> filtered)
    {
        if (filtered.Count == 0 && (HasActiveTimeFilter()
                                    || !string.IsNullOrWhiteSpace(SearchKeyword)
                                    || HasActiveTagFilter()))
            return "无匹配结果";

        return null;
    }

    private bool HasActiveTagFilter() => _activeTagFilters.Count > 0;

    private bool HasActiveTimeFilter() => TimeRange != LogViewTimeRange.All;

    public IReadOnlyList<string> GetFilteredLines()
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(_cachedText))
            return Array.Empty<string>();

        var newline = _cachedText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = _cachedText.Split(newline, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        var searchComparison = SearchCaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        var now = NowProvider?.Invoke() ?? DateTime.Now;
        var activeTags = _activeTagFilters
            .Select(tag => tag.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            if (!LogLineTagParser.TryParse(line, out var tag, out _))
            {
                if (HasActiveTimeFilter() || HasActiveTagFilter())
                    continue;

                var rawLine = $"[RAW] {line}";
                if (!string.IsNullOrEmpty(SearchKeyword) &&
                    !rawLine.Contains(SearchKeyword, searchComparison))
                    continue;

                result.Add(rawLine);
                continue;
            }

            if (HasActiveTagFilter() && !MatchesTagFilter(tag, activeTags))
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

    private static bool MatchesTagFilter(string tag, IReadOnlySet<string> activeTags)
    {
        var normalized = tag.ToUpperInvariant();
        if (activeTags.Contains(normalized))
            return true;

        return normalized == "\u76D1\u63A7\u4E2D" && activeTags.Contains("HEART");
    }
}
