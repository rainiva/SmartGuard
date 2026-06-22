using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class AppDialogTests
{
    [Fact]
    public void ShowConfirm_returns_true_when_primary_action_clicked()
    {
        WpfStaTestHost.Run(() =>
        {
            var owner = CreateOwnerWindow();
            try
            {
                var confirmed = false;
                var dialog = AppDialog.CreateDialogWindow(
                    owner,
                    "Title",
                    "Body",
                    AppDialogSeverity.Warning,
                    showCancel: true,
                    result => confirmed = result);
                var buttons = GetActionButtons(dialog);
                buttons[^1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                confirmed.Should().BeTrue();
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void ShowConfirm_returns_false_when_cancel_clicked()
    {
        WpfStaTestHost.Run(() =>
        {
            var owner = CreateOwnerWindow();
            try
            {
                var confirmed = true;
                var dialog = AppDialog.CreateDialogWindow(
                    owner,
                    "Title",
                    "Body",
                    AppDialogSeverity.Warning,
                    showCancel: true,
                    result => confirmed = result);
                var buttons = GetActionButtons(dialog);
                buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                confirmed.Should().BeFalse();
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void Dialog_uses_flutter_style_rounded_surface()
    {
        WpfStaTestHost.Run(() =>
        {
            var owner = CreateOwnerWindow();
            try
            {
                var dialog = AppDialog.CreateDialogWindow(owner, "Title", "Body", AppDialogSeverity.Information, showCancel: false);
                var surface = (Border)dialog.Content!;
                surface.CornerRadius.TopLeft.Should().BeGreaterThanOrEqualTo(16);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void Dialog_surface_has_no_drop_shadow()
    {
        WpfStaTestHost.Run(() =>
        {
            var owner = CreateOwnerWindow();
            try
            {
                var dialog = AppDialog.CreateDialogWindow(owner, "Title", "Body", AppDialogSeverity.Information, showCancel: false);
                var surface = (Border)dialog.Content!;
                surface.Effect.Should().BeNull();
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void Alert_primary_button_uses_rounded_filled_style()
    {
        WpfStaTestHost.Run(() =>
        {
            var owner = CreateOwnerWindow();
            try
            {
                var dialog = AppDialog.CreateDialogWindow(owner, "Title", "Body", AppDialogSeverity.Information, showCancel: false);
                var primary = GetActionButtons(dialog).Single();
                primary.Content.Should().Be("知道了");
                IsFilledBackground(primary.Background).Should().BeTrue();
                GetButtonCornerRadius(primary).Should().BeGreaterThanOrEqualTo(AppDialogLayout.PrimaryButtonCornerRadius);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void Confirm_cancel_button_keeps_text_style()
    {
        WpfStaTestHost.Run(() =>
        {
            var owner = CreateOwnerWindow();
            try
            {
                var dialog = AppDialog.CreateDialogWindow(owner, "Title", "Body", AppDialogSeverity.Warning, showCancel: true);
                var buttons = GetActionButtons(dialog);
                buttons.Should().HaveCount(2);
                IsTransparentBackground(buttons[0].Background).Should().BeTrue();
                IsFilledBackground(buttons[1].Background).Should().BeTrue();
            }
            finally
            {
                owner.Close();
            }
        });
    }

    private static bool IsTransparentBackground(Brush? brush)
    {
        if (ReferenceEquals(brush, Brushes.Transparent))
            return true;

        return brush is SolidColorBrush { Color.A: 0 };
    }

    private static bool IsFilledBackground(Brush? brush)
        => brush is SolidColorBrush { Color.A: > 0 };

    private static double GetButtonCornerRadius(Button button)
    {
        button.ApplyTemplate();
        if (button.Template?.FindName("bd", button) is Border border)
            return border.CornerRadius.TopLeft;

        return 0;
    }

    private static Window CreateOwnerWindow()
    {
        var owner = new Window
        {
            Width = 400,
            Height = 300,
            ShowInTaskbar = false,
        };
        WpfStaTestHost.ShowAndWait(owner);
        return owner;
    }

    private static List<Button> GetActionButtons(Window dialog)
    {
        var root = (StackPanel)((Border)dialog.Content!).Child;
        var actionPanel = (StackPanel)root.Children[1];
        return actionPanel.Children.OfType<Button>().ToList();
    }
}
