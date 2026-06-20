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
    private readonly Grid _root;
    private bool _isClosing;
    private Storyboard? _activeStoryboard;

    public InlineToastNotification(string message, bool isError, bool isDarkMode, Border container)
    {
        _container = container;

        var iconGlyph = isError ? "\xE783" : "\xE73E";
        var (cardBackground, cardBorder, textForeground, iconBackground, iconForeground) = BuildBrushes(isError, isDarkMode);

        var iconText = new TextBlock
        {
            Text = iconGlyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            Foreground = iconForeground,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var iconCircle = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = iconBackground,
            Child = iconText,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var text = new TextBlock
        {
            Text = message,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
            Foreground = textForeground,
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
        content.Children.Add(iconCircle);
        content.Children.Add(text);
        Grid.SetColumn(text, 1);

        _border = new Border
        {
            Background = cardBackground,
            BorderBrush = cardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 18, 12),
            Margin = new Thickness(8),
            Child = content
        };

        _root = new Grid
        {
            RenderTransform = new TranslateTransform(30, 0),
            Opacity = 0
        };
        _root.Children.Add(_border);

        _container.Child = _root;
        _container.Visibility = Visibility.Visible;
    }

    public static (Brush CardBackground, Brush CardBorder, Brush TextForeground, Brush IconBackground, Brush IconForeground) BuildBrushes(bool isError, bool isDarkMode)
    {
        if (isDarkMode)
        {
            var cardBackground = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));
            var cardBorder = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
            var textForeground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            var iconBackground = new SolidColorBrush(isError ? Color.FromRgb(0x4A, 0x1C, 0x1F) : Color.FromRgb(0x1B, 0x3D, 0x1F));
            var iconForeground = new SolidColorBrush(isError ? Color.FromRgb(0xFF, 0xCD, 0xD2) : Color.FromRgb(0x81, 0xC7, 0x84));
            return (cardBackground, cardBorder, textForeground, iconBackground, iconForeground);
        }

        var lightCardBackground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        var lightCardBorder = new SolidColorBrush(Color.FromRgb(0xE5, 0xE5, 0xE5));
        var lightTextForeground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
        var lightIconBackground = new SolidColorBrush(isError ? Color.FromRgb(0xFF, 0xEB, 0xEE) : Color.FromRgb(0xE8, 0xF5, 0xE9));
        var lightIconForeground = new SolidColorBrush(isError ? Color.FromRgb(0xC6, 0x28, 0x28) : Color.FromRgb(0x2E, 0x7D, 0x32));
        return (lightCardBackground, lightCardBorder, lightTextForeground, lightIconBackground, lightIconForeground);
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
        var transform = (TranslateTransform)_root.RenderTransform;

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
        Storyboard.SetTarget(fade, _root);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(fade);

        storyboard.Completed += (_, _) =>
        {
            // Only hide the container if this toast still owns it.
            if (_container.Child == _root)
            {
                _container.Visibility = Visibility.Collapsed;
                _container.Child = null;
            }
            Closed?.Invoke(this, EventArgs.Empty);
        };
        storyboard.Begin(_root);
    }

    private void PlayEntranceAnimation()
    {
        StopActiveStoryboard();

        var storyboard = new Storyboard();
        var transform = (TranslateTransform)_root.RenderTransform;

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
        Storyboard.SetTarget(fade, _root);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(fade);

        _activeStoryboard = storyboard;
        storyboard.Begin(_root);
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
        var (background, borderBrush, foreground, _, _) = InlineToastNotification.BuildBrushes(isError, isDarkMode);

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DismissCurrent();
    }
}
