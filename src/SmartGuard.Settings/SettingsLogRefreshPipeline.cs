using System.Windows;

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

  internal int ActiveRefreshCount => Volatile.Read(ref _activeRefreshCount);

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
      CancelActiveRefresh();
      _pendingRefresh = true;
      _pendingKind = MergeKind(_pendingKind, kind);
      _pendingForceRefresh |= forceRefresh;
      _pendingForceRedraw |= forceRedraw;
      return;
    }

    StartRefreshFlight(kind, forceRedraw, forceRefresh);
  }

  private void CancelActiveRefresh()
  {
    lock (_refreshLifetimeLock)
      _refreshCancellation?.Cancel();
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
    var filterGeneration = Volatile.Read(ref _filterGeneration);

    _applyFilters(controller);

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
        result = await controller.RefreshAsync(LogViewRefreshKind.Latest, cancellationToken);
      }
      else if (kind == LogViewRefreshKind.Older)
      {
        if (Interlocked.CompareExchange(ref _olderInFlight, 1, 0) != 0)
          return null;

        try
        {
          result = await controller.RefreshAsync(LogViewRefreshKind.Older, cancellationToken);
        }
        finally
        {
          Interlocked.Exchange(ref _olderInFlight, 0);
        }
      }
      else if (kind == LogViewRefreshKind.Refilter)
      {
        result = await controller.RefreshAsync(LogViewRefreshKind.Refilter, cancellationToken);
      }
      else
      {
        controller.RefreshFromDisk();
        result = await controller.RefreshAsync(LogViewRefreshKind.Refilter, cancellationToken);
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
        _setTopLoadingIndicator?.Invoke(false);

      lock (_refreshLifetimeLock)
      {
        if (ReferenceEquals(_activeRefreshTask, refreshTask))
          _activeRefreshTask = null;
        if (ReferenceEquals(_refreshCancellation, refreshCancellation))
          _refreshCancellation = null;
        refreshCancellation.Dispose();
      }

      Interlocked.Decrement(ref _activeRefreshCount);
      Interlocked.Decrement(ref _globalRunningFlightCount);
    }

    if (canceled || _isDisposed() || _window.Dispatcher.HasShutdownStarted)
    {
      FinishRefreshFlight(generation);
      return;
    }

    _ = _window.Dispatcher.BeginInvoke(
      () =>
      {
        MarkStage("ui-dispatched");
        CompleteRefreshOnUi(flightResult, failure, generation, forceRedraw);
      },
      System.Windows.Threading.DispatcherPriority.Background);
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

      if (flightResult.FilterGeneration != Volatile.Read(ref _filterGeneration))
        return;

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
}
