using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartGuard.Settings.Tests;

public class AppDialogTests
{
    [Fact]
    public void ShowConfirm_returns_true_when_primary_action_clicked()
    {
        RunOnSta(() =>
        {
            var owner = new Window { Width = 400, Height = 300, ShowInTaskbar = false };
            owner.Show();

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
            owner.Close();
        });
    }

    [Fact]
    public void ShowConfirm_returns_false_when_cancel_clicked()
    {
        RunOnSta(() =>
        {
            var owner = new Window { Width = 400, Height = 300, ShowInTaskbar = false };
            owner.Show();

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
            owner.Close();
        });
    }

    [Fact]
    public void Dialog_uses_flutter_style_rounded_surface()
    {
        RunOnSta(() =>
        {
            var owner = new Window { Width = 400, Height = 300, ShowInTaskbar = false };
            owner.Show();

            var dialog = AppDialog.CreateDialogWindow(owner, "Title", "Body", AppDialogSeverity.Information, showCancel: false);
            var surface = (Border)dialog.Content!;
            surface.CornerRadius.TopLeft.Should().BeGreaterThanOrEqualTo(16);

            owner.Close();
        });
    }

    [Fact]
    public void Dialog_actions_use_text_button_style_without_filled_background()
    {
        RunOnSta(() =>
        {
            var owner = new Window { Width = 400, Height = 300, ShowInTaskbar = false };
            owner.Show();

            var dialog = AppDialog.CreateDialogWindow(owner, "Title", "Body", AppDialogSeverity.Warning, showCancel: true);
            var buttons = GetActionButtons(dialog);
            buttons.Should().NotBeEmpty();
            buttons.Should().AllSatisfy(button => button.Background.Should().Be(Brushes.Transparent));

            owner.Close();
        });
    }

    private static List<Button> GetActionButtons(Window dialog)
    {
        var root = (StackPanel)((Border)dialog.Content!).Child;
        var actionPanel = (StackPanel)root.Children[1];
        return actionPanel.Children.OfType<Button>().ToList();
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw error;
    }
}
