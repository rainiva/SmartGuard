using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace SmartGuard.Settings;

public interface IToastWindow
{
    void Show();
    void Close();
    event EventHandler? Closed;
}

public interface IToastWindowFactory
{
    IToastWindow Create(string message, bool isError, bool isDarkMode, Window owner);
}

public sealed class InlineToastNotification : IToastWindow
{
    private readonly Border _container;
    private readonly Border _border;
    private bool _isClosing;
    private Storyboard? _activeStoryboard;

    public InlineToastNotification(string message, bool isError, bool isDarkMode, Border container)
    {
        _container = container;

        var iconGlyph = isError ? "\xE783" : "\xE73E";
        var (background, borderBrush, foreground) = BuildBrushes(isError, isDarkMode);

        var icon = new TextBlock
        {
            Text = iconGlyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 16,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };

        var text = new TextBlock
        {
            Text = message,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
            Foreground = foreground,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        var content = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            }
        };
        content.Children.Add(icon);
        content.Children.Add(text);
        Grid.SetColumn(text, 1);

        _border = new Border
        {
            Background = background,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(8, 8, 8, 12),
            RenderTransform = new TranslateTransform(30, 0),
            Opacity = 0,
            Child = content
        };

        // Manual drop shadow avoids WPF clipping the border corners.
        var shadow = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb((byte)(isDarkMode ? 12 : 28), 0, 0, 0)),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(8, 8, 8, -4),
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var root = new Grid();
        root.Children.Add(shadow);
        root.Children.Add(_border);

        _container.Child = root;
        _container.Visibility = Visibility.Visible;
    }

    public static (Brush Background, Brush Border, Brush Foreground) BuildBrushes(bool isError, bool isDarkMode)
    {
        if (isDarkMode)
        {
            var background = new SolidColorBrush(isError ? Color.FromRgb(0x4A, 0x1C, 0x1F) : Color.FromRgb(0x1B, 0x3D, 0x1F));
            var border = new SolidColorBrush(isError ? Color.FromRgb(0xEF, 0x53, 0x50) : Color.FromRgb(0x4C, 0xAF, 0x50));
            var foreground = new SolidColorBrush(isError ? Color.FromRgb(0xFF, 0xCD, 0xD2) : Color.FromRgb(0xC8, 0xE6, 0xC9));
            return (background, border, foreground);
        }

        var lightBackground = new SolidColorBrush(isError ? Color.FromRgb(0xFF, 0xEB, 0xEE) : Color.FromRgb(0xE8, 0xF5, 0xE9));
        var lightBorder = new SolidColorBrush(isError ? Color.FromRgb(0xEF, 0x9A, 0x9A) : Color.FromRgb(0xA5, 0xD6, 0xA7));
        var lightForeground = new SolidColorBrush(isError ? Color.FromRgb(0xB7, 0x1C, 0x1C) : Color.FromRgb(0x1B, 0x5E, 0x20));
        return (lightBackground, lightBorder, lightForeground);
    }

    public void Show()
    {
        PlayEntranceAnimation();
    }

    public void Close()
    {
        if (_isClosing) return;
        _isClosing = true;
        StopActiveStoryboard();

        var storyboard = new Storyboard();
        var transform = (TranslateTransform)_border.RenderTransform;

        var slide = new DoubleAnimation(0, -20, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(slide, transform);
        Storyboard.SetTargetProperty(slide, new PropertyPath(TranslateTransform.YProperty));
        storyboard.Children.Add(slide);

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(fade, _border);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(fade);

        storyboard.Completed += (_, _) =>
        {
            // Only hide the container if this toast still owns it.
            if (_container.Child == _border)
            {
                _container.Visibility = Visibility.Collapsed;
                _container.Child = null;
            }
            Closed?.Invoke(this, EventArgs.Empty);
        };
        storyboard.Begin(_border);
    }

    private void PlayEntranceAnimation()
    {
        StopActiveStoryboard();

        var storyboard = new Storyboard();
        var transform = (TranslateTransform)_border.RenderTransform;

        var slide = new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, transform);
        Storyboard.SetTargetProperty(slide, new PropertyPath(TranslateTransform.XProperty));
        storyboard.Children.Add(slide);

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, _border);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(fade);

        _activeStoryboard = storyboard;
        storyboard.Begin(_border);
    }

    private void StopActiveStoryboard()
    {
        _activeStoryboard?.Stop();
        _activeStoryboard = null;
    }

    public event EventHandler? Closed;
}

public sealed class ToastNotification : IToastWindow
{
    private const double ToastWidth = 260;
    private const double EdgeMargin = 24;

    private readonly Window _window;
    private readonly Border _border;
    private bool _isClosing;
    private Storyboard? _activeStoryboard;

    public ToastNotification(string message, bool isError, bool isDarkMode, Window owner)
    {
        var iconGlyph = isError ? "\xE783" : "\xE73E";
        var (background, borderBrush, foreground) = InlineToastNotification.BuildBrushes(isError, isDarkMode);

        var icon = new TextBlock
        {
            Text = iconGlyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 16,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };

        var text = new TextBlock
        {
            Text = message,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
            Foreground = foreground,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        var content = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            }
        };
        content.Children.Add(icon);
        content.Children.Add(text);
        Grid.SetColumn(text, 1);

        _border = new Border
        {
            Background = background,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(10),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 270,
                ShadowDepth = 4,
                BlurRadius = 12,
                Opacity = isDarkMode ? 0.08 : 0.15
            },
            RenderTransform = new TranslateTransform(30, 0),
            Opacity = 0,
            Child = content
        };

        _window = new Window
        {
            Title = string.Empty,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            Width = ToastWidth,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            Owner = owner,
            Content = _border
        };

        _window.Loaded += OnLoaded;
        _window.Closed += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_window.Owner is null) return;

        _window.Left = _window.Owner.Left + _window.Owner.ActualWidth - ToastWidth - EdgeMargin;
        _window.Top = _window.Owner.Top + SystemParameters.CaptionHeight;

        PlayEntranceAnimation();
    }

    private void PlayEntranceAnimation()
    {
        StopActiveStoryboard();

        var storyboard = new Storyboard();
        var transform = (TranslateTransform)_border.RenderTransform;

        var slide = new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, transform);
        Storyboard.SetTargetProperty(slide, new PropertyPath(TranslateTransform.XProperty));
        storyboard.Children.Add(slide);

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, _border);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(fade);

        _activeStoryboard = storyboard;
        storyboard.Begin(_border);
    }

    private void StopActiveStoryboard()
    {
        _activeStoryboard?.Stop();
        _activeStoryboard = null;
    }

    public void Show()
    {
        _window.Show();
    }

    public void Close()
    {
        if (_isClosing || !_window.IsVisible)
        {
            _window.Close();
            return;
        }

        _isClosing = true;
        StopActiveStoryboard();

        var storyboard = new Storyboard();
        var transform = (TranslateTransform)_border.RenderTransform;

        var slide = new DoubleAnimation(0, -20, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(slide, transform);
        Storyboard.SetTargetProperty(slide, new PropertyPath(TranslateTransform.YProperty));
        storyboard.Children.Add(slide);

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(fade, _border);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(fade);

        storyboard.Completed += (_, _) => _window.Close();
        storyboard.Begin(_border);
    }

    public event EventHandler? Closed;
}

public sealed class ToastNotificationService
{
    private readonly Window _owner;
    private readonly TimeSpan _displayDuration;
    private readonly Func<string, bool, bool, Window, IToastWindow> _factory;
    private IToastWindow? _currentToast;
    private string? _currentMessage;
    private System.Windows.Threading.DispatcherTimer? _closeTimer;

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
        // Idempotent: if the same message is already showing, just reset its timer.
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
}
