using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartGuard.Settings;

public interface IToastWindow
{
    void Show();
    void Close();
    event EventHandler? Closed;
}

public interface IToastWindowFactory
{
    IToastWindow Create(string message, bool isError, Window owner);
}

public sealed class ToastNotification : IToastWindow
{
    private readonly Window _window;

    public ToastNotification(string message, bool isError, Window owner)
    {
        _window = new Window
        {
            Title = string.Empty,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            Width = 240,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            Owner = owner,
            Content = new Border
            {
                Background = new SolidColorBrush(isError ? Color.FromRgb(253, 231, 233) : Color.FromRgb(232, 244, 232)),
                BorderBrush = new SolidColorBrush(isError ? Color.FromRgb(245, 165, 169) : Color.FromRgb(186, 216, 186)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(10),
                Child = new TextBlock
                {
                    Text = message,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(isError ? Color.FromRgb(197, 54, 59) : Color.FromRgb(56, 118, 56))
                }
            }
        };

        _window.Loaded += (_, _) =>
        {
            if (_window.Owner is null) return;
            _window.Left = _window.Owner.Left + _window.Owner.ActualWidth - _window.ActualWidth - 12;
            _window.Top = _window.Owner.Top + 12;
        };

        _window.Closed += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
    }

    public void Show()
    {
        _window.Show();
    }

    public void Close()
    {
        _window.Close();
    }

    public event EventHandler? Closed;
}

public sealed class ToastNotificationService
{
    private readonly Window _owner;
    private readonly TimeSpan _displayDuration;
    private readonly Func<string, bool, Window, IToastWindow> _factory;
    private IToastWindow? _currentToast;
    private string? _currentMessage;
    private System.Windows.Threading.DispatcherTimer? _closeTimer;

    public ToastNotificationService(Window owner, TimeSpan? displayDuration = null)
        : this(owner, displayDuration ?? TimeSpan.FromSeconds(3), (message, isError, windowOwner) => new ToastNotification(message, isError, windowOwner))
    {
    }

    public ToastNotificationService(Window owner, TimeSpan displayDuration, Func<string, bool, Window, IToastWindow> factory)
    {
        _owner = owner;
        _displayDuration = displayDuration;
        _factory = factory;
    }

    public void Show(string message, bool isError)
    {
        // Idempotent: if the same message is already showing, just reset its timer.
        if (_currentToast is not null && _currentMessage == message)
        {
            ResetCloseTimer();
            return;
        }

        DismissCurrent();

        _currentMessage = message;
        _currentToast = _factory(message, isError, _owner);
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
}
