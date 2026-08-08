using System.Windows;
using System.Windows.Threading;

namespace SmartGuard.Settings;

internal sealed class SettingsLogRefreshPipeline
{
  private readonly Window _window;
  private readonly Func<bool> _isDisposed;
  private readonly Func<bool> _isLogPageActive;
  private readonly Func<LogViewController?> _getRefreshableController;
  private readonly Action<LogViewController> _applyFilters;
  private readonly Action<Exception> _showFailure;
  private readonly Action<LogViewRefreshResult, bool> _applyRefreshResult;
  private readonly Action _applyScrollIfEnabled;
  private readonly Action<bool>? _setTopLoadingIndicator;
  private readonly object _refreshLifetimeLock = new();

  private int _refreshGeneration;
  private int _filterGeneration;
  private bool _refreshInFlight;
  private bool _pendingRefresh;
  private LogViewRefreshKind _pendingKind = LogViewRefreshKind.Newer;
  private bool _pendingForceRefresh;
  private bool _pendingForceRedraw;
  private int _olderInFlight;
  private CancellationTokenSource? _refreshCancellation;
  private Task? _activeRefreshTask;
  private PagedFlight? _pendingPagedFlight;
  private Action<LogPaginationApplyModel>? _applyPagedModel;
  private int _activeRefreshCount;
  private int _applyAfterDisposeCount;
  private volatile string _flightStage = "idle";
  private long _flightStageAtUtcTicks = DateTime.UtcNow.Ticks;
  private static int _globalRunningFlightCount;
  private static int _globalStartedFlightCount;

  internal static int GlobalRunningFlightCountForTests => Volatile.Read(ref _globalRunningFlightCount);

  internal static int GlobalStartedFlightCountForTests => Volatile.Read(ref _globalStartedFlightCount);

  internal SettingsLogRefreshPipeline(
    Window window,
    Func<bool> isDisposed,
    Func<bool> isLogPageActive,
    Func<LogViewController?> getRefreshableController,
    Action<LogViewController> applyFilters,
    Action<Exception> showFailure,
    Action<LogViewRefreshResult, bool> applyRefreshResult,
    Action applyScrollIfEnabled,
    Action<bool>? setTopLoadingIndicator = null)
  {
    _window = window;
    _isDisposed = isDisposed;
    _isLogPageActive = isLogPageActive;
    _getRefreshableController = getRefreshableController;
    _applyFilters = applyFilters;
    _showFailure = showFailure;
    _applyRefreshResult = applyRefreshResult;
    _applyScrollIfEnabled = applyScrollIfEnabled;
    _setTopLoadingIndicator = setTopLoadingIndicator;
  }

  internal bool IsRefreshInFlight =>
    !_isDisposed() && (_refreshInFlight || Volatile.Read(ref _activeRefreshCount) > 0);

  internal bool UiRefreshInFlight => _refreshInFlight;

  internal int ActiveRefreshCount => _isDisposed() ? 0 : Volatile.Read(ref _activeRefreshCount);

  internal int ApplyAfterDisposeCount => Volatile.Read(ref _applyAfterDisposeCount);

  internal string FlightStageForTests
  {
    get
    {
      var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Volatile.Read(ref _flightStageAtUtcTicks));
      return $"{_flightStage}+{(int)elapsed.TotalMilliseconds}ms";
    }
  }

  internal void NotifyFilterGenerationChanged() =>
    Interlocked.Increment(ref _filterGeneration);

  private void MarkStage(string stage)
  {
    _flightStage = stage;
    Volatile.Write(ref _flightStageAtUtcTicks, DateTime.UtcNow.Ticks);
  }

  internal void RecordApplyAfterDispose() =>
    Interlocked.Increment(ref _applyAfterDisposeCount);

  internal void Shutdown()
  {
    Interlocked.Increment(ref _refreshGeneration);
    lock (_refreshLifetimeLock)
      _refreshCancellation?.Cancel();
    _refreshInFlight = false;
    _pendingRefresh = false;
    _pendingPagedFlight = null;
    _pendingForceRefresh = false;
    _pendingForceRedraw = false;
    _setTopLoadingIndicator?.Invoke(false);
  }

  internal void Schedule(bool forceRefresh, bool forceRedraw)
    => Schedule(forceRefresh ? LogViewRefreshKind.Latest : LogViewRefreshKind.Newer, forceRedraw, forceRefresh);

  internal void Schedule(LogViewRefreshKind kind, bool forceRedraw = false, bool forceRefresh = false)
  {
    if (_isDisposed() || _getRefreshableController() is null)
      return;

    Interlocked.Increment(ref _refreshGeneration);

    if (_refreshInFlight)
    {
      _pendingRefresh = true;
      _pendingPagedFlight = null;
      _pendingKind = MergeKind(_pendingKind, kind);
      _pendingForceRefresh |= forceRefresh;
      _pendingForceRedraw |= forceRedraw;
      return;
    }

    StartRefreshFlight(kind, forceRedraw, forceRefresh);
  }

  internal void ConfigurePagedApply(Action<LogPaginationApplyModel> applyPagedModel)
    => _applyPagedModel = applyPagedModel ?? throw new ArgumentNullException(nameof(applyPagedModel));

  internal void SchedulePaged(
    LogPaginationIntent intent,
    Func<LogPaginationIntent, IProgress<LogPaginationApplyModel>, CancellationToken, Task> execute)
  {
    ArgumentNullException.ThrowIfNull(intent);
    ArgumentNullException.ThrowIfNull(execute);

    if (_isDisposed() || _getRefreshableController() is null)
      return;

    Interlocked.Increment(ref _refreshGeneration);
    var flight = new PagedFlight(intent, execute);
    if (_refreshInFlight)
    {
      _pendingPagedFlight = flight;
      _pendingRefresh = false;
      _pendingForceRefresh = false;
      _pendingForceRedraw = false;
      return;
    }

    StartPagedFlight(flight);
  }

  private static LogViewRefreshKind MergeKind(LogViewRefreshKind current, LogViewRefreshKind incoming)
  {
    if (incoming == LogViewRefreshKind.Latest || current == LogViewRefreshKind.Latest)
      return LogViewRefreshKind.Latest;
    if (incoming == LogViewRefreshKind.Older || current == LogViewRefreshKind.Older)
      return LogViewRefreshKind.Older;
    if (incoming == LogViewRefreshKind.Refilter || current == LogViewRefreshKind.Refilter)
      return LogViewRefreshKind.Refilter;
    return LogViewRefreshKind.Newer;
  }

  private void StartRefreshFlight(LogViewRefreshKind kind, bool forceRedraw, bool forceRefresh)
  {
    var controller = _getRefreshableController();
    if (_isDisposed() || controller is null)
      return;

    _refreshInFlight = true;
    var generation = Volatile.Read(ref _refreshGeneration);

    _applyFilters(controller);
    var filterGeneration = Volatile.Read(ref _filterGeneration);

    if (kind == LogViewRefreshKind.Older)
      _setTopLoadingIndicator?.Invoke(true);

    var refreshCancellation = new CancellationTokenSource();
    var cancellationToken = refreshCancellation.Token;
    lock (_refreshLifetimeLock)
      _refreshCancellation = refreshCancellation;

    Interlocked.Increment(ref _activeRefreshCount);
    Interlocked.Increment(ref _globalRunningFlightCount);
    Interlocked.Increment(ref _globalStartedFlightCount);
    MarkStage("queued");

    var refreshTask = Task.Run(async () =>
    {
      MarkStage("reading");
      cancellationToken.ThrowIfCancellationRequested();

      LogViewRefreshResult result;
      if (forceRefresh || kind == LogViewRefreshKind.Latest)
      {
        result = await controller.RefreshAsync(LogViewRefreshKind.Latest, cancellationToken)
          .ConfigureAwait(false);
      }
      else if (kind == LogViewRefreshKind.Older)
      {
        if (Interlocked.CompareExchange(ref _olderInFlight, 1, 0) != 0)
          return null;

        try
        {
          result = await controller.RefreshAsync(LogViewRefreshKind.Older, cancellationToken)
            .ConfigureAwait(false);
        }
        finally
        {
          Interlocked.Exchange(ref _olderInFlight, 0);
        }
      }
      else if (kind == LogViewRefreshKind.Refilter)
      {
        result = await controller.RefreshAsync(LogViewRefreshKind.Refilter, cancellationToken)
          .ConfigureAwait(false);
      }
      else
      {
        controller.RefreshFromDisk();
        result = await controller.RefreshAsync(LogViewRefreshKind.Refilter, cancellationToken)
          .ConfigureAwait(false);
      }

      cancellationToken.ThrowIfCancellationRequested();
      MarkStage("read-done");
      return new RefreshFlightResult(result, filterGeneration);
    });

    lock (_refreshLifetimeLock)
      _activeRefreshTask = refreshTask;

    _ = ObserveRefreshCompletionAsync(
      refreshTask,
      refreshCancellation,
      cancellationToken,
      generation,
      forceRedraw,
      kind);
  }

  private void StartPagedFlight(PagedFlight flight)
  {
    if (_isDisposed() || _getRefreshableController() is null)
      return;

    _refreshInFlight = true;
    var generation = Volatile.Read(ref _refreshGeneration);
    var refreshCancellation = new CancellationTokenSource();
    var cancellationToken = refreshCancellation.Token;
    lock (_refreshLifetimeLock)
      _refreshCancellation = refreshCancellation;

    Interlocked.Increment(ref _activeRefreshCount);
    Interlocked.Increment(ref _globalRunningFlightCount);
    Interlocked.Increment(ref _globalStartedFlightCount);
    MarkStage("paged-queued");

    var pagedTask = Task.Run(
      async () =>
      {
        MarkStage("paged-reading");
        cancellationToken.ThrowIfCancellationRequested();
        var progress = new Progress<LogPaginationApplyModel>(
          model => QueuePagedApply(model, generation));
        await flight.Execute(flight.Intent, progress, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
      },
      cancellationToken);

    lock (_refreshLifetimeLock)
      _activeRefreshTask = pagedTask;

    _ = ObservePagedCompletionAsync(
      pagedTask,
      refreshCancellation,
      cancellationToken,
      generation);
  }

  private void QueuePagedApply(LogPaginationApplyModel model, int generation)
  {
    if (_window.Dispatcher.HasShutdownStarted)
      return;

    _ = _window.Dispatcher.BeginInvoke(
      () =>
      {
        if (_isDisposed()
            || !_isLogPageActive()
            || generation != Volatile.Read(ref _refreshGeneration))
          return;

        _applyPagedModel?.Invoke(model);
      },
      DispatcherPriority.Send);
  }

  private async Task ObservePagedCompletionAsync(
    Task pagedTask,
    CancellationTokenSource refreshCancellation,
    CancellationToken cancellationToken,
    int generation)
  {
    Exception? failure = null;
    var canceled = false;

    try
    {
      await pagedTask.ConfigureAwait(false);
      MarkStage("paged-awaited");
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      canceled = true;
    }
    catch (Exception ex)
    {
      failure = ex;
    }
    finally
    {
      lock (_refreshLifetimeLock)
      {
        if (ReferenceEquals(_activeRefreshTask, pagedTask))
          _activeRefreshTask = null;
        if (ReferenceEquals(_refreshCancellation, refreshCancellation))
          _refreshCancellation = null;
      }

      Interlocked.Decrement(ref _activeRefreshCount);
      Interlocked.Decrement(ref _globalRunningFlightCount);
    }

    if (_window.Dispatcher.HasShutdownStarted)
    {
      try { refreshCancellation.Dispose(); } catch (ObjectDisposedException) { }
      FinishRefreshFlight(generation);
      return;
    }

    try
    {
      await _window.Dispatcher.InvokeAsync(
        () =>
        {
          try { refreshCancellation.Dispose(); } catch (ObjectDisposedException) { }

          if (!canceled && !_isDisposed() && failure is not null && generation == Volatile.Read(ref _refreshGeneration))
            _showFailure(failure);

          FinishRefreshFlight(generation);
        },
        DispatcherPriority.Send);
    }
    catch (TaskCanceledException)
    {
      try { refreshCancellation.Dispose(); } catch (ObjectDisposedException) { }
      FinishRefreshFlight(generation);
    }
  }

  private async Task ObserveRefreshCompletionAsync(
    Task<RefreshFlightResult?> refreshTask,
    CancellationTokenSource refreshCancellation,
    CancellationToken cancellationToken,
    int generation,
    bool forceRedraw,
    LogViewRefreshKind kind)
  {
    RefreshFlightResult? flightResult = null;
    Exception? failure = null;
    var canceled = false;

    try
    {
      flightResult = await refreshTask.ConfigureAwait(false);
      MarkStage("awaited");
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      canceled = true;
    }
    catch (Exception ex)
    {
      failure = ex;
    }
    finally
    {
      if (kind == LogViewRefreshKind.Older)
      {
        // Marshal indicator clear onto the UI thread; never touch WPF from here.
        if (!_window.Dispatcher.HasShutdownStarted)
        {
          _ = _window.Dispatcher.BeginInvoke(
            () => _setTopLoadingIndicator?.Invoke(false),
            DispatcherPriority.Send);
        }
      }

      lock (_refreshLifetimeLock)
      {
        if (ReferenceEquals(_activeRefreshTask, refreshTask))
          _activeRefreshTask = null;
        if (ReferenceEquals(_refreshCancellation, refreshCancellation))
          _refreshCancellation = null;
      }

      Interlocked.Decrement(ref _activeRefreshCount);
      Interlocked.Decrement(ref _globalRunningFlightCount);
    }

    if (_window.Dispatcher.HasShutdownStarted)
    {
      try { refreshCancellation.Dispose(); } catch (ObjectDisposedException) { }
      FinishRefreshFlight(generation);
      return;
    }

    try
    {
      await _window.Dispatcher.InvokeAsync(
        () =>
        {
          try { refreshCancellation.Dispose(); } catch (ObjectDisposedException) { }

          if (canceled || _isDisposed())
          {
            FinishRefreshFlight(generation);
            return;
          }

          MarkStage("ui-dispatched");
          CompleteRefreshOnUi(flightResult, failure, generation, forceRedraw);
        },
        DispatcherPriority.Send);
    }
    catch (TaskCanceledException)
    {
      try { refreshCancellation.Dispose(); } catch (ObjectDisposedException) { }
      FinishRefreshFlight(generation);
    }
  }

  private void CompleteRefreshOnUi(
    RefreshFlightResult? flightResult,
    Exception? failure,
    int generation,
    bool forceRedraw)
  {
    try
    {
      if (_isDisposed() || !_isLogPageActive())
        return;

      if (failure is not null)
      {
        _showFailure(failure);
        return;
      }

      if (flightResult is null)
        return;

      // Always apply the newest completed flight. Coalesced requests restart
      // another flight in FinishRefreshFlight; discarding here livelocks the
      // page on "正在加载" when refreshes arrive faster than UI completion.
      _applyRefreshResult(flightResult.Result, forceRedraw);
      _applyScrollIfEnabled();
    }
    finally
    {
      MarkStage("completed");
      FinishRefreshFlight(generation);
    }
  }

  private void FinishRefreshFlight(int generation)
  {
    _refreshInFlight = false;
    if (_isDisposed())
      return;

    if (_pendingPagedFlight is { } pendingPagedFlight)
    {
      _pendingPagedFlight = null;
      _pendingRefresh = false;
      _pendingKind = LogViewRefreshKind.Newer;
      _pendingForceRefresh = false;
      _pendingForceRedraw = false;
      StartPagedFlight(pendingPagedFlight);
      return;
    }

    if (_pendingRefresh || Volatile.Read(ref _refreshGeneration) != generation)
    {
      var nextKind = _pendingKind;
      var nextForceRefresh = _pendingForceRefresh;
      var nextForceRedraw = _pendingForceRedraw;
      _pendingRefresh = false;
      _pendingKind = LogViewRefreshKind.Newer;
      _pendingForceRefresh = false;
      _pendingForceRedraw = false;
      StartRefreshFlight(nextKind, nextForceRedraw, nextForceRefresh);
    }
  }

  private sealed record RefreshFlightResult(LogViewRefreshResult Result, int FilterGeneration);

  private sealed record PagedFlight(
    LogPaginationIntent Intent,
    Func<LogPaginationIntent, IProgress<LogPaginationApplyModel>, CancellationToken, Task> Execute);
}
