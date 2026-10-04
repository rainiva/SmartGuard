namespace SmartGuard.Settings;

internal enum DiagnosticExportOrigin { About, Logs }

/// <summary>Owns one window's diagnostic flight, including its save dialog.
/// Cancellation never releases admission before the worker actually exits.</summary>
internal sealed class SettingsDiagnosticExportSession : IDisposable
{
  private readonly object _gate = new();
  private CancellationTokenSource? _cancellation;
  private DiagnosticExportOrigin? _origin;
  private bool _disposed;
  internal event Action? StateChanged;
  public bool IsRunning { get { lock (_gate) return _cancellation is not null; } }
  public bool IsAlive { get { lock (_gate) return !_disposed; } }
  public DiagnosticExportOrigin? ActiveOrigin { get { lock (_gate) return _origin; } }

  public async Task RunAsync(DiagnosticExportOrigin origin, Func<CancellationToken, Task> export)
  {
    CancellationTokenSource cancellation;
    lock (_gate)
    {
      if (_disposed || _cancellation is not null) return;
      _cancellation = cancellation = new CancellationTokenSource();
      _origin = origin;
    }
    Notify();
    try { await export(cancellation.Token); }
    finally
    {
      lock (_gate) { _cancellation = null; _origin = null; }
      cancellation.Dispose();
      Notify();
    }
  }

  public void Cancel(DiagnosticExportOrigin? expectedOrigin = null)
  {
    CancellationTokenSource? cancellation;
    lock (_gate) cancellation = expectedOrigin is null || expectedOrigin == _origin ? _cancellation : null;
    try { cancellation?.Cancel(); } catch (ObjectDisposedException) { }
  }

  public void Dispose()
  {
    lock (_gate) _disposed = true;
    Cancel();
    StateChanged = null;
  }

  private void Notify()
  {
    if (!IsAlive) return;
    // UI observers cannot strand the owned flight or reverse its result.
    try { StateChanged?.Invoke(); } catch { }
  }
}
