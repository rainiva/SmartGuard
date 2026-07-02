using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace SmartGuard.Settings;

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
      FontSize = ToastLayoutMetrics.FloatingIconFontSize,
      Foreground = foreground,
      VerticalAlignment = VerticalAlignment.Center,
      Margin = new Thickness(0, 0, 8, 0)
    };

    var text = new TextBlock
    {
      Text = message,
      FontSize = ToastLayoutMetrics.FloatingFontSize,
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
      CornerRadius = new CornerRadius(ToastLayoutMetrics.FloatingCornerRadius),
      Padding = new Thickness(
        ToastLayoutMetrics.FloatingPaddingHorizontal,
        ToastLayoutMetrics.FloatingPaddingVertical,
        ToastLayoutMetrics.FloatingPaddingHorizontal,
        ToastLayoutMetrics.FloatingPaddingVertical),
      Margin = new Thickness(8),
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

  public void Show() => _window.Show();

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
