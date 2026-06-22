using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class ScrollBarAutoHideTests
{
    [Fact]
    public void Log_list_enables_virtualization_in_xaml()
    {
        var xaml = ReadSettingsXaml();

        xaml.Should().Contain("x:Name=\"lstLogView\"");
        xaml.Should().Contain("VirtualizingPanel.IsVirtualizing=\"True\"");
        xaml.Should().Contain("VirtualizingPanel.VirtualizationMode=\"Recycling\"");
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
        WpfStaTestHost.Run(() =>
        {
            var host = new Window { ShowInTaskbar = false };
            try
            {
                var scrollViewer = CreateStyledScrollViewer();
                host.Content = scrollViewer;
                WpfStaTestHost.ShowAndWait(host);
                LayoutScrollViewer(scrollViewer);

                ScrollBarAutoHide.Attach(scrollViewer);
                ScrollBarAutoHide.NotifyScrollActivity(scrollViewer);

                ScrollBarAutoHide.IsScrollBarVisible(scrollViewer, vertical: true).Should().BeTrue();

                ScrollBarAutoHide.HideNowForTesting(scrollViewer);
                ScrollBarAutoHide.IsScrollBarVisible(scrollViewer, vertical: true).Should().BeFalse();
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public void Horizontal_scrollbar_stays_visible_when_log_content_overflows()
    {
        WpfStaTestHost.Run(() =>
        {
            var host = new Window
            {
                Width = 360,
                Height = 280,
                ShowInTaskbar = false,
            };

            try
            {
                var scrollViewer = CreateStyledScrollViewer(new TextBlock
                {
                    Text = new string('x', 400),
                    TextWrapping = TextWrapping.NoWrap,
                });
                host.Content = scrollViewer;
                WpfStaTestHost.ShowAndWait(host);
                LayoutScrollViewer(scrollViewer);

                ScrollBarAutoHide.Attach(scrollViewer);
                ScrollBarAutoHide.NotifyContentChanged(scrollViewer);

                scrollViewer.ScrollableWidth.Should().BeGreaterThan(0);
                ScrollBarAutoHide.IsScrollBarVisible(scrollViewer, vertical: false).Should().BeTrue();
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public void Horizontal_scrollbar_track_is_not_direction_reversed()
    {
        var xaml = ReadSettingsXaml();

        xaml.Should().MatchRegex(
            "x:Key=\"SettingsScrollBar\"[\\s\\S]{0,900}Orientation, RelativeSource=\\{RelativeSource TemplatedParent\\}\\}\" Value=\"Vertical\"[\\s\\S]{0,120}IsDirectionReversed\" Value=\"True\"",
            "only vertical scrollbars should reverse direction");
        xaml.Should().MatchRegex(
            "x:Key=\"SettingsScrollBar\"[\\s\\S]{0,900}Setter Property=\"IsDirectionReversed\" Value=\"False\"",
            "horizontal scrollbars should keep the default left-to-right mapping");
    }

    [Fact]
    public void Horizontal_offset_zero_keeps_scrollbar_thumb_at_start()
    {
        WpfStaTestHost.Run(() =>
        {
            var host = new Window
            {
                Width = 360,
                Height = 280,
                ShowInTaskbar = false,
            };

            try
            {
                var scrollViewer = CreateStyledScrollViewer(new TextBlock
                {
                    Text = "START-" + new string('x', 500) + "-END",
                    TextWrapping = TextWrapping.NoWrap,
                });
                host.Content = scrollViewer;
                WpfStaTestHost.ShowAndWait(host);
                LayoutScrollViewer(scrollViewer);

                scrollViewer.ScrollableWidth.Should().BeGreaterThan(0);
                scrollViewer.ScrollToHorizontalOffset(0);
                scrollViewer.UpdateLayout();

                var horizontalScrollBar = FindHorizontalScrollBar(scrollViewer);
                horizontalScrollBar.Should().NotBeNull();
                horizontalScrollBar!.Value.Should().Be(horizontalScrollBar.Minimum);

                scrollViewer.ScrollToHorizontalOffset(scrollViewer.ScrollableWidth);
                scrollViewer.UpdateLayout();
                horizontalScrollBar.Value.Should().Be(horizontalScrollBar.Maximum);
            }
            finally
            {
                host.Close();
            }
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

    private static Style LoadSettingsScrollViewerStyle()
    {
        WpfStaTestHost.EnsureApplication();
        var templateWindow = (Window)Application.LoadComponent(
            new Uri("/SmartGuard.Settings;component/SmartGuard.Settings.xaml", UriKind.Relative));
        try
        {
            var styled = templateWindow.FindName("contentScrollViewer") as ScrollViewer;
            styled.Should().NotBeNull();
            return styled!.Style!;
        }
        finally
        {
            templateWindow.Close();
        }
    }

    private static ScrollViewer CreateStyledScrollViewer(UIElement? content = null)
    {
        return new ScrollViewer
        {
            Width = 320,
            Height = 240,
            Style = LoadSettingsScrollViewerStyle(),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content ?? new TextBlock
            {
                Text = string.Join(Environment.NewLine, Enumerable.Repeat("log line", 80)),
                TextWrapping = TextWrapping.NoWrap,
            },
        };
    }

    private static void LayoutScrollViewer(ScrollViewer scrollViewer)
    {
        scrollViewer.ApplyTemplate();
        scrollViewer.UpdateLayout();
        scrollViewer.Measure(new Size(scrollViewer.Width, scrollViewer.Height));
        scrollViewer.Arrange(new Rect(0, 0, scrollViewer.Width, scrollViewer.Height));
        scrollViewer.UpdateLayout();
        scrollViewer.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static ScrollBar? FindHorizontalScrollBar(ScrollViewer scrollViewer)
    {
        scrollViewer.ApplyTemplate();
        return scrollViewer.Template?.FindName("PART_HorizontalScrollBar", scrollViewer) as ScrollBar;
    }
}
