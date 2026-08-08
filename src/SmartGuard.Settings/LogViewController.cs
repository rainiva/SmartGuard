using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings;

public sealed class LogViewController
{
    private readonly ILogEventSource _eventSource;
    private readonly string _logPath;
    private readonly string? _fallbackLogPath;
    private readonly LogEventWindow _eventWindow = new();

    private LogReadCursor? _olderCursor;
    private LogReadCursor? _newerCursor;
    private bool _hasOlderHistory;
    private bool _hasNewerHistory;
    private bool _isTailTruncated;
    private bool _isLoaded;
    private bool _contentChanged;
    private bool _loadFailed;
    private string? _loadFailureMessage;
    private long _trackedFileLength = -1;
    private LogWindowDelta _lastDelta = LogWindowDelta.None;
    private IReadOnlyList<string> _activeTagFilters = [];

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

    internal LogQueryExpression? ScopeExpression { get; set; }

    internal int CachedTextLengthForTests => _eventWindow.EstimatedPayloadBytes;

    internal int LoadedEventCountForTests => _eventWindow.Count;

    internal LogEventWindow EventWindowForTests => _eventWindow;

    public LogViewController(string logPath, string? fallbackLogPath = null)
        : this(new LogTailReader(logPath, fallbackLogPath), logPath, fallbackLogPath)
    {
    }

    internal LogViewController(ILogEventSource eventSource, string logPath, string? fallbackLogPath = null)
    {
        _eventSource = eventSource;
        _logPath = logPath;
        _fallbackLogPath = fallbackLogPath;
    }

    public async Task<LogViewRefreshResult> RefreshAsync(LogViewRefreshKind kind, CancellationToken cancellationToken)
    {
        _contentChanged = false;
        try
        {
            var structuralDelta = kind switch
            {
                LogViewRefreshKind.Latest => await LoadLatestAsync(cancellationToken),
                LogViewRefreshKind.Older => await LoadOlderAsync(cancellationToken),
                LogViewRefreshKind.Newer => await LoadNewerAsync(cancellationToken),
                LogViewRefreshKind.Refilter => Refilter(),
                LogViewRefreshKind.HistoryStart => await LoadHistoryBoundaryAsync(
                    LogReadDirection.Older,
                    cancellationToken),
                LogViewRefreshKind.HistoryEnd => await LoadHistoryBoundaryAsync(
                    LogReadDirection.Newer,
                    cancellationToken),
                _ => LogWindowDelta.None,
            };

            _loadFailed = false;
            _loadFailureMessage = null;
            _isLoaded = true;
            _lastDelta = ToDisplayDelta(structuralDelta, kind);
            if (!_lastDelta.IsEmpty)
                _contentChanged = true;

            return CreateRefreshResult();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _loadFailed = true;
            _loadFailureMessage = ex.Message;
            _lastDelta = LogWindowDelta.None;
            _isLoaded = true;
            return CreateRefreshResult();
        }
    }

    public LogViewRefreshResult CreateRefreshResult()
        => new(_lastDelta, BuildSnapshot(), _contentChanged);

    public void ForceRefresh()
    {
        RefreshAsync(LogViewRefreshKind.Latest, CancellationToken.None).GetAwaiter().GetResult();
        _contentChanged = true;
    }

    public void RefreshFromDisk()
    {
        EnsureLoaded();
        _contentChanged = false;
        _lastDelta = LogWindowDelta.None;

        var length = LogTailReader.GetFileLength(_logPath);
        if (length <= 0)
        {
            if (_trackedFileLength > 0)
                ForceRefresh();
            else if (_trackedFileLength < 0)
                _trackedFileLength = 0;

            return;
        }

        if (_trackedFileLength >= 0 && length == _trackedFileLength)
            return;

        if (_trackedFileLength < 0 || length < _trackedFileLength)
        {
            ForceRefresh();
            return;
        }

        if (length > _trackedFileLength)
        {
            _trackedFileLength = length;
            if (FollowTail)
            {
                var result = RefreshAsync(LogViewRefreshKind.Newer, CancellationToken.None).GetAwaiter().GetResult();
                _contentChanged = result.ContentChanged;
            }
        }
    }

    public LogViewSnapshot GetSnapshot() => BuildSnapshot();

    internal ILogEventSource EventSourceForCorrelation => _eventSource;

    internal LogSourceCatalog? CatalogSnapshotForDiagnostics =>
      _eventSource is LogTailReader reader ? reader.Catalog : null;

    internal IReadOnlyList<LogEvent> WindowEventsForDiagnostics =>
      _eventWindow.Events.ToArray();

    internal void LoadCorrelatedEvents(IReadOnlyList<LogEvent> events)
    {
        _olderCursor = null;
        _newerCursor = null;
        _hasOlderHistory = false;
        _hasNewerHistory = false;
        _isTailTruncated = false;
        _loadFailed = false;
        _loadFailureMessage = null;
        _isLoaded = true;
        _lastDelta = _eventWindow.Initialize(events);
        _contentChanged = true;
    }

    public IReadOnlyList<string> GetFilteredLines()
    {
        EnsureLoaded();
        return LogViewEventFilter.FilterLines(_eventWindow.Events, GetFilterState());
    }

    public void ApplyFilterSpec(LogQueryFilterSpec spec)
    {
        SearchKeyword = spec.Keyword ?? string.Empty;
        OperationIdFilter = spec.OperationId ?? string.Empty;
        EventNameFilter = spec.EventName ?? string.Empty;
        ProcessIdFilter = spec.ProcessId;
        ThreadIdFilter = spec.ThreadId;
        if (!string.IsNullOrEmpty(spec.Level))
            ActiveTagFilters = [spec.Level];
        if (spec.StartTime.HasValue || spec.EndTime.HasValue)
        {
            CustomRangeStart = spec.StartTime?.LocalDateTime;
            CustomRangeEnd = spec.EndTime?.LocalDateTime;
            TimeRange = LogViewTimeRange.Custom;
        }
    }

    public Task<IReadOnlyList<string>> GetFilteredLinesAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        return Task.Run(() => GetFilteredLines(), cancellationToken);
    }

    private async Task<LogWindowDelta> LoadLatestAsync(CancellationToken cancellationToken)
    {
        var page = await _eventSource.ReadLatestAsync(
            CreateReadRequest(null, LogReadDirection.Latest),
            cancellationToken);
        UpdateFromPage(page);
        _eventWindow.SetAnchorEventId(null);
        return _eventWindow.Initialize(page.Events);
    }

    private async Task<LogWindowDelta> LoadOlderAsync(CancellationToken cancellationToken)
    {
        if (!_hasOlderHistory && _olderCursor is null)
            return LogWindowDelta.None;

        var page = await _eventSource.ReadOlderAsync(
            CreateReadRequest(_olderCursor, LogReadDirection.Older),
            cancellationToken);
        if (page.Events.Count == 0)
            return LogWindowDelta.None;

        UpdateFromPage(page);
        return _eventWindow.Prepend(page.Events);
    }

    private async Task<LogWindowDelta> LoadNewerAsync(CancellationToken cancellationToken)
    {
        if (!_hasNewerHistory && _newerCursor is null && _isLoaded)
            return LogWindowDelta.None;

        var page = await _eventSource.ReadNewerAsync(
            CreateReadRequest(_newerCursor, LogReadDirection.Newer),
            cancellationToken);
        if (page.Events.Count == 0)
            return LogWindowDelta.None;

        UpdateFromPage(page);
        _trackedFileLength = LogTailReader.GetFileLength(_logPath);
        return _eventWindow.Append(page.Events);
    }

    private async Task<LogWindowDelta> LoadHistoryBoundaryAsync(
        LogReadDirection direction,
        CancellationToken cancellationToken)
    {
        var changed = false;
        if (!_isLoaded)
        {
            await LoadLatestAsync(cancellationToken);
            changed = true;
        }

        while (direction == LogReadDirection.Older
            ? _hasOlderHistory || _olderCursor is not null
            : _hasNewerHistory || _newerCursor is not null)
        {
            var page = direction == LogReadDirection.Older
                ? await _eventSource.ReadOlderAsync(
                    CreateReadRequest(_olderCursor, direction),
                    cancellationToken)
                : await _eventSource.ReadNewerAsync(
                    CreateReadRequest(_newerCursor, direction),
                    cancellationToken);

            UpdateFromPage(page);
            if (page.Events.Count == 0)
                break;

            if (direction == LogReadDirection.Older)
                _eventWindow.Prepend(page.Events);
            else
                _eventWindow.Append(page.Events);

            changed = true;
        }

        return changed
            ? LogWindowDelta.Initialize(_eventWindow.Events)
            : LogWindowDelta.None;
    }

    private LogWindowDelta Refilter()
    {
        EnsureLoaded();
        return ToDisplayDelta(LogWindowDelta.None, LogViewRefreshKind.Refilter);
    }

    private LogWindowDelta ToDisplayDelta(LogWindowDelta structuralDelta, LogViewRefreshKind kind)
    {
        if (kind != LogViewRefreshKind.Refilter && !GetFilterState().HasActiveFilters)
            return structuralDelta;

        var filtered = LogViewEventFilter.FilterEvents(_eventWindow.Events, GetFilterState());
        return LogWindowDelta.Initialize(filtered);
    }

    private LogViewSnapshot BuildSnapshot()
    {
        var filteredLines = GetFilteredLines();
        return new LogViewSnapshot(
            filteredLines,
            _eventWindow.Count,
            _isTailTruncated,
            _logPath,
            _contentChanged,
            SearchKeyword,
            ResolveEmptyStateMessage(filteredLines),
            TimeRange,
            filteredLines.Count,
            IsDisplayTruncated: false,
            _activeTagFilters,
            _hasOlderHistory,
            _loadFailed,
            _loadFailureMessage);
    }

    private void EnsureLoaded()
    {
        if (_isLoaded || _loadFailed)
            return;

        ForceRefresh();
    }

    private void UpdateFromPage(LogEventPage page)
    {
        _olderCursor = page.OlderCursor;
        _newerCursor = page.NewerCursor;
        _hasOlderHistory = page.HasOlder;
        _hasNewerHistory = page.HasNewer;
        _trackedFileLength = LogTailReader.GetFileLength(_logPath);
        _isTailTruncated = page.HasOlder
            || LogTailReader.ReadInitialView(_logPath, _fallbackLogPath, LogTailReader.DefaultMaxBytes).IsTailTruncated;
    }

    private LogReadRequest CreateReadRequest(LogReadCursor? cursor, LogReadDirection direction)
        => new(
            cursor,
            direction,
            LogTailReader.DefaultTargetEventCount,
            LogTailReader.DefaultMaxBytes);

    private LogViewFilterState GetFilterState()
        => LogViewFilterState.FromLegacyFields(
            SearchKeyword,
            SearchCaseSensitive,
            _activeTagFilters,
            TimeRange,
            CustomRangeStart,
            CustomRangeEnd,
            OperationIdFilter,
            EventNameFilter,
            ProcessIdFilter,
            ThreadIdFilter,
            NowProvider)
            .WithScope(ScopeExpression);

    private string? ResolveEmptyStateMessage(IReadOnlyList<string> filtered)
    {
        if (filtered.Count == 0 && GetFilterState().HasActiveFilters)
            return "无匹配结果";

        return null;
    }
}
