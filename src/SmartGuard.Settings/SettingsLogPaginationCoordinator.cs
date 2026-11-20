using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal sealed class SettingsLogPaginationCoordinator : IDisposable
{
  private readonly Action<
    LogPaginationIntent,
    Func<LogPaginationIntent, IProgress<LogPaginationApplyModel>, CancellationToken, Task>> _schedulePaged;
  private readonly Func<
    LogPaginationIntent,
    LogSourceSnapshot?,
    LogQueryPageIndex?,
    IProgress<LogPaginationApplyModel>,
    CancellationToken,
    Task> _execute;
  private readonly Func<bool> _isDisposed;

  private LogPaginationState _state = LogPaginationState.Initial;
  private LogSourceSnapshot? _currentSnapshot;
  private LogQueryPageIndex? _currentIndex;
  private IReadOnlyList<LogEvent> _currentEvents = Array.Empty<LogEvent>();
  private LogQueryExpression? _expression;
  private LogReadScope? _scope;
  private bool _ignoreCase;
  private LogSortDirection _sortDirection = LogSortDirection.NewestFirst;
  private string? _lastError;
  private bool _disposed;

  internal SettingsLogPaginationCoordinator(
    Action<
      LogPaginationIntent,
      Func<LogPaginationIntent, IProgress<LogPaginationApplyModel>, CancellationToken, Task>> schedulePaged,
    Func<
      LogPaginationIntent,
      LogSourceSnapshot?,
      LogQueryPageIndex?,
      IProgress<LogPaginationApplyModel>,
      CancellationToken,
      Task> execute,
    Func<bool> isDisposed)
  {
    _schedulePaged = schedulePaged ?? throw new ArgumentNullException(nameof(schedulePaged));
    _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    _isDisposed = isDisposed ?? throw new ArgumentNullException(nameof(isDisposed));
  }

  internal LogPaginationState State => _state;

  internal LogSortDirection SortDirection => _sortDirection;

  internal LogSourceSnapshot? CurrentSnapshot => _currentSnapshot;

  internal LogQueryPageIndex? CurrentIndex => _currentIndex;

  internal IReadOnlyList<LogEvent> CurrentEvents => _currentEvents;

  internal string? LastError => _lastError;

  internal Task RefreshAsync(LogQueryExpression expression, LogReadScope scope, bool ignoreCase)
  {
    SetQuery(expression, scope, ignoreCase);
    _currentSnapshot = null;
    _currentIndex = null;
    _lastError = null;
    _sortDirection = LogSortDirection.NewestFirst;
    _state = _state.ResetForExplicitRefresh().StartNavigation(1);
    return ScheduleAsync(captureNewSnapshot: true);
  }

  internal Task ApplyFilterAsync(
    LogQueryExpression expression,
    LogReadScope scope,
    bool ignoreCase,
    LogSortDirection? sortDirection = null)
  {
    SetQuery(expression, scope, ignoreCase);
    _currentIndex = null;
    _lastError = null;
    if (sortDirection is LogSortDirection requestedSortDirection)
      _sortDirection = requestedSortDirection;
    _state = _state.ResetForFilterChange().StartNavigation(1);
    return ScheduleAsync(captureNewSnapshot: false);
  }

  internal Task ApplySessionAsync(
    LogQueryExpression expression,
    LogReadScope scope,
    bool ignoreCase,
    LogSortDirection? sortDirection = null)
    => ApplyFilterAsync(expression, scope, ignoreCase, sortDirection);

  internal Task GoToPageAsync(int pageNumber)
  {
    EnsureQueryConfigured();
    _state = _state.StartNavigation(pageNumber);
    return ScheduleAsync(captureNewSnapshot: false);
  }

  internal Task ChangePageSizeAsync(int pageSize)
  {
    EnsureQueryConfigured();
    _state = _state.ChangePageSize(pageSize).StartNavigation(1);
    return ScheduleAsync(captureNewSnapshot: false);
  }

  internal Task ToggleSortAsync()
  {
    EnsureQueryConfigured();
    _sortDirection = _sortDirection == LogSortDirection.NewestFirst
      ? LogSortDirection.OldestFirst
      : LogSortDirection.NewestFirst;
    _state = _state.ResetForSortChange().StartNavigation(1);
    return ScheduleAsync(captureNewSnapshot: false);
  }

  internal void MarkSnapshotStale()
  {
    if (_disposed || _isDisposed())
      return;

    _state = _state.FailGeneration();
  }

  internal void ApplyModel(LogPaginationApplyModel model)
  {
    // SettingsLogRefreshPipeline has already discarded stale generations before
    // dispatching this callback; it is the only flight-generation owner.
    if (_disposed || _isDisposed())
      return;

    if (model.ErrorMessage is not null)
    {
      _lastError = model.ErrorMessage;
      _state = _state.FailGeneration();
      return;
    }

    _lastError = null;
    _currentEvents = model.Events;
    _currentSnapshot = model.Snapshot;
    if (model.Index is not null)
      _currentIndex = model.Index;

    if (model.IsProvisional)
    {
      _state = _state.MarkCountInexact(model.IsScanComplete);
      return;
    }

    if (model.ExactTotalCount is not int exactTotalCount)
    {
      _lastError = "分页结果缺少精确总数。";
      _state = _state.FailGeneration();
      return;
    }

    _state = _state.ApplyExactCount(exactTotalCount, model.IsScanComplete);
    if (model.PageNumber != _state.CurrentPage)
    {
      _state = _state
        .StartNavigation(model.PageNumber)
        .ApplyExactCount(exactTotalCount, model.IsScanComplete);
    }
  }

  public void Dispose() => _disposed = true;

  private Task ScheduleAsync(bool captureNewSnapshot)
  {
    EnsureQueryConfigured();
    var intent = new LogPaginationIntent(
      QueryGeneration: 0,
      _expression!,
      _scope!,
      _state.CurrentPage,
      _state.PageSize,
      _sortDirection,
      _ignoreCase,
      captureNewSnapshot);
    var snapshot = _currentSnapshot;
    var index = _currentIndex;
    _schedulePaged(
      intent,
      (scheduledIntent, progress, cancellationToken) =>
        _execute(scheduledIntent, snapshot, index, progress, cancellationToken));
    return Task.CompletedTask;
  }

  private void SetQuery(LogQueryExpression expression, LogReadScope scope, bool ignoreCase)
  {
    _expression = expression ?? throw new ArgumentNullException(nameof(expression));
    _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    _ignoreCase = ignoreCase;
  }

  private void EnsureQueryConfigured()
  {
    if (_expression is null || _scope is null)
      throw new InvalidOperationException("分页查询尚未配置表达式和日志范围。");
  }
}
