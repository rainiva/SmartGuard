using System.Windows;

namespace SmartGuard.Settings;

public sealed class ToastNotificationService : IDisposable
{
  private readonly Window _owner;
  private readonly TimeSpan _displayDuration;
  private readonly Func<string, bool, bool, Window, IToastWindow> _factory;
  private IToastWindow? _currentToast;
  private string? _currentMessage;
  private System.Windows.Threading.DispatcherTimer? _closeTimer;
  private bool _disposed;

  public bool IsDarkMode { get; set; }

  public ToastNotificationService(Window owner, TimeSpan? displayDuration = null)
    : this(owner, displayDuration ?? TimeSpan.FromSeconds(3), (message, isError, isDarkMode, windowOwner) => new ToastNotification(message, isError, isDarkMode, windowOwner))
  {
  }

  public ToastNotificationService(Window owner, TimeSpan displayDuration, Func<string, bool, bool, Window, IToastWindow> factory)
  {
    _owner = owner;
    _displayDuration = displayDuration;
    _factory = factory;
  }

  public void Show(string message, bool isError)
  {
    if (_currentToast is not null && _currentMessage == message)
    {
      ResetCloseTimer();
      return;
    }

    DismissCurrent();

    _currentMessage = message;
    _currentToast = _factory(message, isError, IsDarkMode, _owner);
    _currentToast.Closed += (_, _) => DismissCurrent();

    _currentToast.Show();

    StartCloseTimer();
  }

  private void DismissCurrent()
  {
    _closeTimer?.Stop();
    _closeTimer = null;

    var toast = _currentToast;
    _currentToast = null;
    _currentMessage = null;
    toast?.Close();
  }

  private void ResetCloseTimer()
  {
    if (_closeTimer is null) return;
    _closeTimer.Stop();
    _closeTimer.Start();
  }

  private void StartCloseTimer()
  {
    _closeTimer = new System.Windows.Threading.DispatcherTimer(
      _displayDuration,
      System.Windows.Threading.DispatcherPriority.Background,
      (_, _) => DismissCurrent(),
      _owner.Dispatcher);
    _closeTimer.Start();
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    DismissCurrent();
  }
}
