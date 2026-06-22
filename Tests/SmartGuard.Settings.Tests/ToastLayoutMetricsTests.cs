using System.Windows;
using System.Windows.Controls;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class ToastLayoutMetricsTests
{
    [Fact]
    public void Inline_toast_uses_compact_vertical_padding()
    {
        ToastLayoutMetrics.InlinePaddingVertical.Should().BeLessThanOrEqualTo(8);
        ToastLayoutMetrics.InlinePaddingHorizontal.Should().BeLessThanOrEqualTo(12);
    }

    [Fact]
    public void Inline_toast_uses_smaller_icon_and_text()
    {
        ToastLayoutMetrics.InlineIconSize.Should().BeLessThanOrEqualTo(22);
        ToastLayoutMetrics.InlineFontSize.Should().BeLessThanOrEqualTo(13);
    }

    [Fact]
    public void Inline_toast_border_has_reduced_vertical_padding()
    {
        RunOnSta(() =>
        {
            var container = new Border();
            var toast = new InlineToastNotification("saved", isError: false, isDarkMode: false, container);
            toast.Show();

            var border = FindChild<Border>(container);
            border.Should().NotBeNull();
            border!.Padding.Top.Should().BeLessThanOrEqualTo(8);
            border.Padding.Bottom.Should().BeLessThanOrEqualTo(8);
        });
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;

            var nested = FindChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static void RunOnSta(Action action) => WpfStaTestHost.Run(action);
}
