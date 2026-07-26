using System.Windows;

namespace SmartGuard.Settings;

// 日志刷新状态机：调度合并、后台读取、UI 线程完成与生命周期取消。
// 由 SettingsLogPageHost 拥有；Dispose 语义经 Shutdown 传入。
internal sealed class SettingsLogRefreshPipeline
{
  private readonly Window _window;
  private readonly Func<bool> _isDisposed;
  private readonly Func<bool> _isLogPageActive;
  private readonly Func<LogViewController?> _getRefreshableController;
  private readonly Action<LogViewController> _applyFilters;
  private readonly Action<Exception> _showFailure;
  private readonly Action<LogViewSnapshot, bool> _applySnapshot;
  private readonly Action _applyScrollIfEnabled;
  private readonly object _refreshLifetimeLock = new();

  private int _refreshGeneration;
  private bool _refreshInFlight;
  private bool _pendingRefresh;
  private bool _pendingForceRefresh;
  private bool _pendingForceRedraw;
  private CancellationTokenSource? _refreshCancellation;
  private Task? _activeRefreshTask;
  private int _activeRefreshCount;
  private int _applyAfterDisposeCount;
  // 诊断插桩：记录最近一次 flight 的阶段与时刻，仅供测试超时信息定位卡点。
  private volatile string _flightStage = "idle";
  private long _flightStageAtUtcTicks = DateTime.UtcNow.Ticks;

  internal SettingsLogRefreshPipeline(
    Window window,
    Func<bool> isDisposed,
    Func<bool> isLogPageActive,
    Func<LogViewController?> getRefreshableController,
    Action<LogViewController> applyFilters,
    Action<Exception> showFailure,
    Action<LogViewSnapshot, bool> applySnapshot,
    Action applyScrollIfEnabled)
  {
    _window = window;
    _isDisposed = isDisposed;
    _isLogPageActive = isLogPageActive;
    _getRefreshableController = getRefreshableController;
    _applyFilters = applyFilters;
    _showFailure = showFailure;
    _applySnapshot = applySnapshot;
    _applyScrollIfEnabled = applyScrollIfEnabled;
  }

  internal bool IsRefreshInFlight =>
    _refreshInFlight || Volatile.Read(ref _activeRefreshCount) > 0;

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
  }

  internal void Schedule(bool forceRefresh, bool forceRedraw)
  {
    if (_isDisposed() || _getRefreshableController() is null)
      return;

    Interlocked.Increment(ref _refreshGeneration);

    if (_refreshInFlight)
    {
      _pendingRefresh = true;
      _pendingForceRefresh |= forceRefresh;
      _pendingForceRedraw |= forceRedraw;
      return;
    }

    StartRefreshFlight(forceRefresh, forceRedraw);
  }

  private void StartRefreshFlight(bool forceRefresh, bool forceRedraw)
  {
    var controller = _getRefreshableController();
    if (_isDisposed() || controller is null)
      return;

    _refreshInFlight = true;
    var generation = Volatile.Read(ref _refreshGeneration);

    _applyFilters(controller);

    var refreshCancellation = new CancellationTokenSource();
    var cancellationToken = refreshCancellation.Token;
    lock (_refreshLifetimeLock)
      _refreshCancellation = refreshCancellation;

    Interlocked.Increment(ref _activeRefreshCount);
    MarkStage("queued");
    var refreshTask = Task.Run(() =>
    {
      MarkStage("reading");
      cancellationToken.ThrowIfCancellationRequested();
      if (forceRefresh)
        controller.ForceRefresh();
      else
        controller.RefreshFromDisk();
      cancellationToken.ThrowIfCancellationRequested();
      MarkStage("snapshotting");
      var result = controller.GetSnapshot();
      MarkStage("read-done");
      return result;
    });
    lock (_refreshLifetimeLock)
      _activeRefreshTask = refreshTask;
    _ = ObserveRefreshCompletionAsync(
      refreshTask,
      refreshCancellation,
      cancellationToken,
      generation,
      forceRedraw);
  }

  private async Task ObserveRefreshCompletionAsync(
    Task<LogViewSnapshot> refreshTask,
    CancellationTokenSource refreshCancellation,
    CancellationToken cancellationToken,
    int generation,
    bool forceRedraw)
  {
    LogViewSnapshot? snapshot = null;
    Exception? failure = null;
    var canceled = false;

    try
    {
      snapshot = await refreshTask.ConfigureAwait(false);
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
      lock (_refreshLifetimeLock)
      {
        if (ReferenceEquals(_activeRefreshTask, refreshTask))
          _activeRefreshTask = null;
        if (ReferenceEquals(_refreshCancellation, refreshCancellation))
          _refreshCancellation = null;
        refreshCancellation.Dispose();
      }
      Interlocked.Decrement(ref _activeRefreshCount);
    }

    if (canceled || _isDisposed() || cancellationToken.IsCancellationRequested)
      return;

    _ = _window.Dispatcher.BeginInvoke(() =>
    {
      MarkStage("ui-dispatched");
      if (_isDisposed() || cancellationToken.IsCancellationRequested)
        return;

      CompleteRefreshOnUi(snapshot, failure, cancellationToken, generation, forceRedraw);
    });
  }

  private void CompleteRefreshOnUi(
    LogViewSnapshot? snapshot,
    Exception? failure,
    CancellationToken cancellationToken,
    int generation,
    bool forceRedraw)
  {
    if (_isDisposed() || cancellationToken.IsCancellationRequested)
      return;

    try
    {
      // Flights are serialized by _refreshInFlight, so this snapshot is
      // always the newest completed result. A stale generation only means
      // another refresh request arrived meanwhile; the finally block
      // restarts a flight for it. Discarding the result here instead would
      // livelock the page ("loading" forever) whenever requests keep
      // arriving faster than completions, e.g. the 2s auto-refresh timer
      // on a loaded machine.
      if (!_isLogPageActive())
        return;

      if (failure is not null)
      {
        _showFailure(failure);
        return;
      }

      if (snapshot is null)
        return;

      _applySnapshot(snapshot, forceRedraw);
      _applyScrollIfEnabled();
    }
    finally
    {
      MarkStage("completed");
      _refreshInFlight = false;
      if (!_isDisposed() && (_pendingRefresh || Volatile.Read(ref _refreshGeneration) != generation))
      {
        var nextForceRefresh = _pendingForceRefresh;
        var nextForceRedraw = _pendingForceRedraw;
        _pendingRefresh = false;
        _pendingForceRefresh = false;
        _pendingForceRedraw = false;
        StartRefreshFlight(nextForceRefresh, nextForceRedraw);
      }
    }
  }
}
