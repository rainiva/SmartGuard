using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SmartGuard.Settings;

public enum AppDialogSeverity
{
    Information,
    Warning,
    Error,
}

public static class AppDialog
{
    public static void ShowAlert(Window? owner, string title, string message, AppDialogSeverity severity)
    {
        ShowDialog(owner, title, message, severity, showCancel: false);
    }

    public static bool ShowConfirm(Window? owner, string title, string message, AppDialogSeverity severity)
        => ShowDialog(owner, title, message, severity, showCancel: true);

    private static bool ShowDialog(
        Window? owner,
        string title,
        string message,
        AppDialogSeverity severity,
        bool showCancel)
    {
        var confirmed = false;
        Window? dialog = null;
        dialog = CreateDialogWindow(owner, title, message, severity, showCancel, result =>
        {
            confirmed = result;
        });
        dialog.ShowDialog();
        return confirmed;
    }

    internal static Window CreateDialogWindow(
        Window? owner,
        string title,
        string message,
        AppDialogSeverity severity,
        bool showCancel,
        Action<bool>? onClose = null)
    {
        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = owner is null
                ? new SolidColorBrush(Colors.Black)
                : ResolveBrush(owner, "TextPrimary", Colors.Black),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var messageBlock = new TextBlock
        {
            Text = message,
            FontSize = 14,
            Foreground = owner is null
                ? new SolidColorBrush(Color.FromRgb(0x5C, 0x5C, 0x5C))
                : ResolveBrush(owner, "TextSecondary", Color.FromRgb(0x5C, 0x5C, 0x5C)),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
        };

        var contentPanel = new StackPanel();
        contentPanel.Children.Add(titleBlock);
        contentPanel.Children.Add(messageBlock);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0),
        };

        if (showCancel)
        {
            var cancelButton = CreateTextButton("取消", owner);
            cancelButton.Click += (_, _) =>
            {
                onClose?.Invoke(false);
                if (Window.GetWindow(cancelButton) is Window window)
                    window.Close();
            };
            actionPanel.Children.Add(cancelButton);
        }

        var primaryLabel = showCancel ? "确定" : "知道了";
        var primaryButton = CreatePrimaryButton(primaryLabel, owner);
        primaryButton.Click += (_, _) =>
        {
            onClose?.Invoke(true);
            if (Window.GetWindow(primaryButton) is Window window)
                window.Close();
        };
        actionPanel.Children.Add(primaryButton);

        var root = new StackPanel();
        root.Children.Add(contentPanel);
        root.Children.Add(actionPanel);

        var surface = new Border
        {
            Background = owner is null
                ? new SolidColorBrush(Colors.White)
                : ResolveBrush(owner, "CardBackground", Colors.White),
            BorderBrush = owner is null
                ? new SolidColorBrush(Color.FromRgb(0xE5, 0xE5, 0xE5))
                : ResolveBrush(owner, "CardBorderBrush", Color.FromRgb(0xE5, 0xE5, 0xE5)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(24, 20, 24, 16),
            MinWidth = 280,
            MaxWidth = 420,
            Child = root,
        };

        var dialog = new Window
        {
            Title = string.Empty,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = surface,
        };

        TryAssignOwner(dialog, owner);
        return dialog;
    }

    private static void TryAssignOwner(Window dialog, Window? owner)
    {
        if (owner is null)
            return;

        if (owner.IsLoaded && owner.IsVisible)
        {
            dialog.Owner = owner;
            return;
        }

        void OnOwnerReady(object? _, EventArgs __)
        {
            owner.Loaded -= OnOwnerReady;
            owner.IsVisibleChanged -= OnOwnerVisibleChanged;
            if (owner.IsVisible)
                dialog.Owner = owner;
        }

        void OnOwnerVisibleChanged(object? _, DependencyPropertyChangedEventArgs __)
        {
            if (!owner.IsVisible)
                return;

            owner.IsVisibleChanged -= OnOwnerVisibleChanged;
            owner.Loaded -= OnOwnerReady;
            dialog.Owner = owner;
        }

        owner.Loaded += OnOwnerReady;
        owner.IsVisibleChanged += OnOwnerVisibleChanged;
    }

    private static Button CreatePrimaryButton(string label, Window? owner)
    {
        var button = new Button
        {
            Content = label,
            Background = owner is null
                ? new SolidColorBrush(Color.FromRgb(0xD6, 0xEB, 0xFF))
                : ResolveBrush(owner, "DialogPrimaryButtonBackground", Color.FromRgb(0xD6, 0xEB, 0xFF)),
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
        button.Template = CreatePrimaryButtonTemplate();
        return button;
    }

    private static ControlTemplate CreatePrimaryButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border), "bd");
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(AppDialogLayout.PrimaryButtonCornerRadius));
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
        return template;
    }

    private static Button CreateTextButton(string label, Window? owner, bool isPrimary = false)
    {
        return new Button
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
    }

    private static SolidColorBrush ResolveBrush(Window? owner, string key, Color fallback)
    {
        if (owner is not null && owner.TryFindResource(key) is SolidColorBrush brush)
            return brush;

        return new SolidColorBrush(fallback);
    }
}
