using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SmartGuard.Settings;

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
      FontSize = ToastLayoutMetrics.InlineIconFontSize,
      Foreground = iconForeground,
      VerticalAlignment = VerticalAlignment.Center,
      HorizontalAlignment = HorizontalAlignment.Center
    };

    var iconCircle = new Border
    {
      Width = ToastLayoutMetrics.InlineIconSize,
      Height = ToastLayoutMetrics.InlineIconSize,
      CornerRadius = new CornerRadius(ToastLayoutMetrics.InlineIconSize / 2),
      Background = iconBackground,
      Child = iconText,
      VerticalAlignment = VerticalAlignment.Center,
      HorizontalAlignment = HorizontalAlignment.Center
    };

    var text = new TextBlock
    {
      Text = message,
      FontSize = ToastLayoutMetrics.InlineFontSize,
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
      CornerRadius = new CornerRadius(ToastLayoutMetrics.InlineCornerRadius),
      Padding = new Thickness(
        ToastLayoutMetrics.InlinePaddingHorizontal,
        ToastLayoutMetrics.InlinePaddingVertical,
        ToastLayoutMetrics.InlinePaddingHorizontal + 4,
        ToastLayoutMetrics.InlinePaddingVertical),
      Margin = new Thickness(
        ToastLayoutMetrics.InlineOuterMargin,
        ToastLayoutMetrics.InlineOuterMargin,
        0,
        ToastLayoutMetrics.InlineOuterMargin),
      Child = content
    };

    _root = new Grid
    {
      RenderTransform = new TranslateTransform(-16, 0),
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

  public void Show() => PlayEntranceAnimation();

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

    var slide = new DoubleAnimation(-16, 0, TimeSpan.FromMilliseconds(250))
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
