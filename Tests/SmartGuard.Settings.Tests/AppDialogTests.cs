using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
    public void Dialog_actions_use_text_button_style_without_filled_background()
    {
        WpfStaTestHost.Run(() =>
        {
            var owner = CreateOwnerWindow();
            try
            {
                var dialog = AppDialog.CreateDialogWindow(owner, "Title", "Body", AppDialogSeverity.Warning, showCancel: true);
                var buttons = GetActionButtons(dialog);
                buttons.Should().NotBeEmpty();
                buttons.Should().AllSatisfy(button => IsTransparentBackground(button.Background));
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
