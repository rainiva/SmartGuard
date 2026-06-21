using System.IO;
using System.Windows;
using System.Windows.Controls;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class ScrollBarAutoHideTests
{
    [Fact]
    public void Log_scroll_viewer_enables_auto_hide_scrollbars_in_xaml()
    {
        var xaml = ReadSettingsXaml();

        xaml.Should().MatchRegex(
            "x:Name=\"logScrollViewer\"[\\s\\S]{0,260}ScrollBarAutoHide\\.IsEnabled=\"True\"",
            "log viewer should hide scrollbars until the user scrolls");
    }

    [Fact]
    public void Settings_content_scroll_viewer_enables_auto_hide_scrollbars_in_xaml()
    {
        var xaml = ReadSettingsXaml();

        xaml.Should().MatchRegex(
            "x:Name=\"contentScrollViewer\"[\\s\\S]{0,260}ScrollBarAutoHide\\.IsEnabled=\"True\"",
            "settings pages should hide scrollbars until the user scrolls");
    }

    [Fact]
    public void User_scrolls_log_viewer_scrollbars_become_visible_then_hide()
    {
        RunOnSta(() =>
        {
            EnsureApplication();

            var scrollViewer = CreateStyledScrollViewer();
            ScrollBarAutoHide.Attach(scrollViewer);
            ScrollBarAutoHide.NotifyScrollActivity(scrollViewer);

            ScrollBarAutoHide.IsScrollBarVisible(scrollViewer, vertical: true).Should().BeTrue();

            ScrollBarAutoHide.HideNowForTesting(scrollViewer);
            ScrollBarAutoHide.IsScrollBarVisible(scrollViewer, vertical: true).Should().BeFalse();
        });
    }

    [Fact]
    public void Horizontal_scrollbar_stays_visible_when_log_content_overflows()
    {
        RunOnSta(() =>
        {
            EnsureApplication();

            var scrollViewer = CreateStyledScrollViewer();
            scrollViewer.Content = new TextBlock
            {
                Text = new string('x', 400),
                TextWrapping = TextWrapping.NoWrap,
            };

            var host = new Window
            {
                Width = 360,
                Height = 280,
                Content = scrollViewer,
            };
            host.Show();
            scrollViewer.UpdateLayout();

            ScrollBarAutoHide.Attach(scrollViewer);
            ScrollBarAutoHide.NotifyContentChanged(scrollViewer);

            scrollViewer.ScrollableWidth.Should().BeGreaterThan(0);
            ScrollBarAutoHide.IsScrollBarVisible(scrollViewer, vertical: false).Should().BeTrue();

            host.Close();
        });
    }

    private static string ReadSettingsXaml()
    {
        var assemblyLocation = typeof(SettingsWindowController).Assembly.Location;
        var repoRoot = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(assemblyLocation)!,
            "..", "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(repoRoot, "lib", "SmartGuard.Settings.xaml"));
    }

    private static ScrollViewer CreateStyledScrollViewer()
    {
        var window = (Window)Application.LoadComponent(
            new Uri("/SmartGuard.Settings;component/SmartGuard.Settings.xaml", UriKind.Relative));
        var styled = window.FindName("logScrollViewer") as ScrollViewer;
        styled.Should().NotBeNull();

        var scrollViewer = new ScrollViewer
        {
            Width = 320,
            Height = 240,
            Style = styled!.Style,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = string.Join(Environment.NewLine, Enumerable.Repeat("log line", 80)),
                TextWrapping = TextWrapping.NoWrap,
            },
        };

        var host = new Window
        {
            Width = 360,
            Height = 280,
            Content = scrollViewer,
        };
        host.Show();
        scrollViewer.ApplyTemplate();
        scrollViewer.UpdateLayout();
        host.Close();

        return scrollViewer;
    }

    private static void EnsureApplication()
    {
        if (Application.Current is not null)
            return;

        try { _ = new Application(); }
        catch (InvalidOperationException) { }
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
