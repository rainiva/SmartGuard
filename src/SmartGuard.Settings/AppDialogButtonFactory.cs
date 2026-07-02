using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SmartGuard.Settings;

internal static class AppDialogButtonFactory
{
  internal static Button CreatePrimaryButton(string label, Window? owner)
  {
    var button = new Button
    {
      Content = label,
      Background = owner is null
        ? new SolidColorBrush(AppDialogLayout.PrimaryButtonBackgroundFallback)
        : ResolveBrush(owner, "DialogPrimaryButtonBackground", AppDialogLayout.PrimaryButtonBackgroundFallback),
      Foreground = owner is null
        ? new SolidColorBrush(Color.FromRgb(0x00, 0x5F, 0xB8))
        : ResolveBrush(owner, "TextAccent", Color.FromRgb(0x00, 0x5F, 0xB8)),
      BorderThickness = new Thickness(0),
      FontSize = 14,
      FontWeight = FontWeights.SemiBold,
      Padding = new Thickness(16, 8, 16, 8),
      MinWidth = 72,
      Cursor = System.Windows.Input.Cursors.Hand,
    };
    var hoverBackground = owner is null
      ? new SolidColorBrush(AppDialogLayout.PrimaryButtonHoverBackgroundFallback)
      : ResolveBrush(owner, "DialogPrimaryButtonHoverBackground", AppDialogLayout.PrimaryButtonHoverBackgroundFallback);
    button.Template = CreatePrimaryButtonTemplate(hoverBackground);
    return button;
  }

  internal static Button CreateTextButton(string label, Window? owner, bool isPrimary = false)
  {
    var button = new Button
    {
      Content = label,
      Background = Brushes.Transparent,
      BorderThickness = new Thickness(0),
      Foreground = owner is null
        ? new SolidColorBrush(Color.FromRgb(0x00, 0x5F, 0xB8))
        : ResolveBrush(owner, isPrimary ? "TextAccent" : "TextSecondary", Color.FromRgb(0x00, 0x5F, 0xB8)),
      FontSize = 14,
      FontWeight = isPrimary ? FontWeights.SemiBold : FontWeights.Normal,
      Padding = new Thickness(12, 8, 12, 8),
      MinWidth = 64,
      Cursor = System.Windows.Input.Cursors.Hand,
    };
    var hoverBackground = owner is null
      ? new SolidColorBrush(AppDialogLayout.TextButtonHoverBackgroundFallback)
      : ResolveBrush(owner, "NavigationItemHoverBackground", AppDialogLayout.TextButtonHoverBackgroundFallback);
    button.Template = CreateTextButtonTemplate(hoverBackground);
    return button;
  }

  internal static SolidColorBrush ResolveBrush(Window? owner, string key, Color fallback)
  {
    if (owner is not null && owner.TryFindResource(key) is SolidColorBrush brush)
      return brush;

    return new SolidColorBrush(fallback);
  }

  private static ControlTemplate CreatePrimaryButtonTemplate(Brush hoverBackground)
  {
    var template = new ControlTemplate(typeof(Button));
    var borderFactory = new FrameworkElementFactory(typeof(Border), "bd");
    borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(AppDialogLayout.PrimaryButtonCornerRadius));
    borderFactory.SetValue(Border.RenderTransformOriginProperty, new Point(0.5, 0.5));
    borderFactory.SetBinding(Border.BackgroundProperty, new Binding(nameof(Button.Background))
    {
      RelativeSource = RelativeSource.TemplatedParent,
    });
    borderFactory.SetBinding(Border.PaddingProperty, new Binding(nameof(Button.Padding))
    {
      RelativeSource = RelativeSource.TemplatedParent,
    });

    var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
    contentFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
    contentFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
    borderFactory.AppendChild(contentFactory);
    template.VisualTree = borderFactory;

    var hoverTrigger = new Trigger
    {
      Property = UIElement.IsMouseOverProperty,
      Value = true,
    };
    hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hoverBackground) { TargetName = "bd" });
    template.Triggers.Add(hoverTrigger);

    var pressedTrigger = new Trigger
    {
      Property = Button.IsPressedProperty,
      Value = true,
    };
    pressedTrigger.Setters.Add(new Setter(Border.RenderTransformProperty, new ScaleTransform(0.98, 0.98))
    {
      TargetName = "bd",
    });
    template.Triggers.Add(pressedTrigger);

    return template;
  }

  private static ControlTemplate CreateTextButtonTemplate(Brush hoverBackground)
  {
    var template = new ControlTemplate(typeof(Button));
    var borderFactory = new FrameworkElementFactory(typeof(Border), "bd");
    borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(AppDialogLayout.TextButtonCornerRadius));
    borderFactory.SetValue(Border.RenderTransformOriginProperty, new Point(0.5, 0.5));
    borderFactory.SetBinding(Border.BackgroundProperty, new Binding(nameof(Button.Background))
    {
      RelativeSource = RelativeSource.TemplatedParent,
    });
    borderFactory.SetBinding(Border.PaddingProperty, new Binding(nameof(Button.Padding))
    {
      RelativeSource = RelativeSource.TemplatedParent,
    });

    var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
    contentFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
    contentFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
    borderFactory.AppendChild(contentFactory);
    template.VisualTree = borderFactory;

    var hoverTrigger = new Trigger
    {
      Property = UIElement.IsMouseOverProperty,
      Value = true,
    };
    hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hoverBackground) { TargetName = "bd" });
    template.Triggers.Add(hoverTrigger);

    var pressedTrigger = new Trigger
    {
      Property = Button.IsPressedProperty,
      Value = true,
    };
    pressedTrigger.Setters.Add(new Setter(Border.RenderTransformProperty, new ScaleTransform(0.98, 0.98))
    {
      TargetName = "bd",
    });
    template.Triggers.Add(pressedTrigger);

    return template;
  }
}
