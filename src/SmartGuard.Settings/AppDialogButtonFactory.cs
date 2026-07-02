using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SmartGuard.Settings;

internal static class AppDialogButtonFactory
{
  internal static Button CreatePrimaryButton(string label, Window? owner)
  {
    var button = new Button { Content = label };
    if (TryApplyStyle(button, owner, "PrimaryButton"))
      return button;

    return CreatePrimaryButtonFallback(label, owner);
  }

  internal static Button CreateSecondaryButton(string label, Window? owner)
  {
    var button = new Button { Content = label };
    if (TryApplyStyle(button, owner, "SecondaryButton"))
      return button;

    return CreateSecondaryButtonFallback(label, owner);
  }

  internal static SolidColorBrush ResolveBrush(Window? owner, string key, Color fallback)
  {
    if (owner is not null && owner.TryFindResource(key) is SolidColorBrush brush)
      return brush;

    return new SolidColorBrush(fallback);
  }

  private static bool TryApplyStyle(Button button, Window? owner, string styleKey)
  {
    if (owner?.TryFindResource(styleKey) is not Style style)
      return false;

    button.Style = style;
    return true;
  }

  private static Button CreatePrimaryButtonFallback(string label, Window? owner)
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

  private static Button CreateSecondaryButtonFallback(string label, Window? owner)
  {
    var button = new Button
    {
      Content = label,
      Background = owner is null
        ? new SolidColorBrush(Colors.White)
        : ResolveBrush(owner, "SecondaryButtonBackground", Colors.White),
      Foreground = owner is null
        ? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A))
        : ResolveBrush(owner, "SecondaryButtonForeground", Color.FromRgb(0x1A, 0x1A, 0x1A)),
      BorderBrush = owner is null
        ? new SolidColorBrush(Color.FromRgb(0xD1, 0xD1, 0xD1))
        : ResolveBrush(owner, "SecondaryButtonBorderBrush", Color.FromRgb(0xD1, 0xD1, 0xD1)),
      BorderThickness = new Thickness(1),
      FontSize = 14,
      FontWeight = FontWeights.Normal,
      Padding = new Thickness(16, 8, 16, 8),
      MinWidth = 72,
      Cursor = System.Windows.Input.Cursors.Hand,
    };
    var hoverBackground = owner is null
      ? new SolidColorBrush(AppDialogLayout.TextButtonHoverBackgroundFallback)
      : ResolveBrush(owner, "SecondaryButtonHoverBackground", AppDialogLayout.TextButtonHoverBackgroundFallback);
    button.Template = CreateSecondaryButtonTemplate(hoverBackground);
    return button;
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

  private static ControlTemplate CreateSecondaryButtonTemplate(Brush hoverBackground)
  {
    var template = new ControlTemplate(typeof(Button));
    var borderFactory = new FrameworkElementFactory(typeof(Border), "bd");
    borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(AppDialogLayout.SecondaryButtonCornerRadius));
    borderFactory.SetValue(Border.RenderTransformOriginProperty, new Point(0.5, 0.5));
    borderFactory.SetBinding(Border.BackgroundProperty, new Binding(nameof(Button.Background))
    {
      RelativeSource = RelativeSource.TemplatedParent,
    });
    borderFactory.SetBinding(Border.BorderBrushProperty, new Binding(nameof(Button.BorderBrush))
    {
      RelativeSource = RelativeSource.TemplatedParent,
    });
    borderFactory.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(Button.BorderThickness))
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
