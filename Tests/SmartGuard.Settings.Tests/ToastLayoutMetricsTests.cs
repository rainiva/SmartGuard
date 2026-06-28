using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
    public void Toast_container_right_margin_aligns_with_content_page()
    {
        ToastLayoutMetrics.ToastContainerRightMargin.Should().Be(
            ToastLayoutMetrics.ContentPageRightMargin);
    }

    [Fact]
    public void Toast_container_lives_in_content_column_not_full_window()
    {
        var xaml = ReadSettingsXaml();
        xaml.Should().MatchRegex(
            "x:Name=\"toastContainer\"[\\s\\S]{0,160}Grid\\.Column=\"1\"",
            "toast overlay should align against the content column, not the full window width");
        xaml.Should().NotMatchRegex(
            "x:Name=\"toastContainer\"[\\s\\S]{0,160}Grid\\.ColumnSpan=\"2\"",
            "toast overlay should not span the navigation column");
    }

    [Fact]
    public void Inline_toast_border_does_not_add_extra_right_inset()
    {
        RunOnSta(() =>
        {
            var container = new Border();
            var toast = new InlineToastNotification("saved", isError: false, isDarkMode: false, container);
            toast.Show();

            var border = FindToastCardBorder(container);
            border.Should().NotBeNull();
            border!.Margin.Right.Should().Be(0);
        });
    }

    [Fact]
    public void Settings_xaml_uses_toast_container_margin_resource()
    {
        var xaml = ReadSettingsXaml();
        xaml.Should().Contain("x:Key=\"ToastContainerMargin\"");
        xaml.Should().MatchRegex(
            "x:Name=\"toastContainer\"[\\s\\S]{0,220}Margin=\"\\{StaticResource ToastContainerMargin\\}\"",
            "toast overlay should align its right edge with the content cards below");
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

    private static Border? FindToastCardBorder(Border container)
    {
        var root = container.Child as FrameworkElement;
        if (root?.RenderTransform is TranslateTransform)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<Border>())
                return child;
        }

        return FindChild<Border>(container);
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

    private static string ReadSettingsXaml()
    {
        var assemblyLocation = typeof(SettingsWindowController).Assembly.Location;
        var repoRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(assemblyLocation)!,
            "..", "..", "..", "..", ".."));
        return System.IO.File.ReadAllText(System.IO.Path.Combine(repoRoot, "lib", "SmartGuard.Settings.xaml"));
    }
}
