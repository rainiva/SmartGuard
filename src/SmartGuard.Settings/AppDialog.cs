using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

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
        var isDark = owner?.TryFindResource("WindowBackground") is SolidColorBrush background
                     && background.Color.R <= 0x40;

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
        var primaryButton = CreateTextButton(primaryLabel, owner, isPrimary: true);
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
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 270,
                ShadowDepth = 2,
                BlurRadius = 16,
                Opacity = isDark ? 0.35 : 0.18,
            },
            Child = root,
        };

        return new Window
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
            Owner = owner,
            ShowInTaskbar = false,
            Content = surface,
        };
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
