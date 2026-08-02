using System.IO;
using SmartGuard.Settings.LogView;

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

    // D2：按文件签名（LastWriteTimeUtc + Length）缓存磁盘总行数，避免 2s 定时刷新重算。
    // 假设：日志文件仅通过追加写增长，且不在时间戳精度窗口内做同长覆盖写。
    // 在该前提下，LastWriteTimeUtc + Length 作为签名可唯一标识内容版本。
    // 如需引入覆盖写或非 NTFS 文件系统，请调整签名逻辑或移除缓存。
    private string? _totalLineCountPath;
    private long _totalLineCountLength = -1;
    private DateTime _totalLineCountWriteTimeUtc;
    private int _totalLineCountCache;

    public string SearchKeyword { get; set; } = string.Empty;
    public bool SearchCaseSensitive { get; set; }
    public string OperationIdFilter { get; set; } = string.Empty;
    public string EventNameFilter { get; set; } = string.Empty;
    public int? ProcessIdFilter { get; set; }
    public int? ThreadIdFilter { get; set; }
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
            preferTail: true);

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

            // 阶段四新增筛选字段
            if (!string.IsNullOrEmpty(OperationIdFilter) &&
                !line.Contains("op=" + OperationIdFilter, searchComparison))
                continue;

            if (!string.IsNullOrEmpty(EventNameFilter) &&
                !line.Contains(EventNameFilter, searchComparison))
                continue;

            result.Add(line);
        }

        return result;
    }

    public void ApplyFilterSpec(SmartGuard.Configuration.LogQueryFilterSpec spec)
    {
      SearchKeyword = spec.Keyword ?? string.Empty;
      OperationIdFilter = spec.OperationId ?? string.Empty;
      EventNameFilter = spec.EventName ?? string.Empty;
      ProcessIdFilter = spec.ProcessId;
      ThreadIdFilter = spec.ThreadId;
      if (!string.IsNullOrEmpty(spec.Level))
        ActiveTagFilters = new[] { spec.Level };
      if (spec.StartTime.HasValue || spec.EndTime.HasValue)
      {
        CustomRangeStart = spec.StartTime?.LocalDateTime;
        CustomRangeEnd = spec.EndTime?.LocalDateTime;
        TimeRange = LogViewTimeRange.Custom;
      }
    }

    public Task<IReadOnlyList<string>> GetFilteredLinesAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => GetFilteredLines(), cancellationToken);
    }

    private int GetTotalLineCount()
    {
        // D6：「总计」只计主日志 _logPath 磁盘真实行数；
        // 仅当主日志不存在时才退化计 _fallbackLogPath。
        string? path = null;
        if (File.Exists(_logPath))
            path = _logPath;
        else if (!string.IsNullOrWhiteSpace(_fallbackLogPath) && File.Exists(_fallbackLogPath))
            path = _fallbackLogPath;

        if (path is null)
            return 0;

        try
        {
            // D2：签名（LastWriteTimeUtc + Length）未变则直接返回缓存。
            var info = new FileInfo(path);
            if (_totalLineCountPath == path
                && _totalLineCountLength == info.Length
                && _totalLineCountWriteTimeUtc == info.LastWriteTimeUtc)
                return _totalLineCountCache;

            var count = LogLineCounter.CountLines(path);
            _totalLineCountPath = path;
            _totalLineCountLength = info.Length;
            _totalLineCountWriteTimeUtc = info.LastWriteTimeUtc;
            _totalLineCountCache = count;
            return count;
        }
        catch
        {
            return 0;
        }
    }

    private static bool MatchesTagFilter(string tag, IReadOnlySet<string> activeTags)
    {
        var normalized = tag.ToUpperInvariant();
        if (activeTags.Contains(normalized))
            return true;

        return normalized == "\u76D1\u63A7\u4E2D" && activeTags.Contains("HEART");
    }
}
