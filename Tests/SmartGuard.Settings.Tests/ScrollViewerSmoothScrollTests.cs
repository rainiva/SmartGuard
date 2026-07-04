using System.IO;
using System.Windows;
using System.Windows.Controls;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class ScrollViewerSmoothScrollTests
{
    [Fact]
    public void Wheel_impulse_starts_inertial_scroll_that_continues_after_input_stops()
    {
        WpfStaTestHost.Run(() =>
        {
            var host = new Window { Width = 420, Height = 320, ShowInTaskbar = false };
            try
            {
                var scrollViewer = CreateTallScrollViewer();
                host.Content = scrollViewer;
                WpfStaTestHost.ShowAndWait(host);
                LayoutScrollViewer(scrollViewer);

                ScrollViewerSmoothScroll.Attach(scrollViewer);
                scrollViewer.VerticalOffset.Should().BeApproximately(0, 1.0);

                ScrollViewerSmoothScroll.SimulateWheelImpulseForTesting(scrollViewer, delta: -120);
                ScrollViewerSmoothScroll.GetVerticalVelocityForTesting(scrollViewer)
                    .Should().BeGreaterThan(0);

                var offsetAfterImpulse = scrollViewer.VerticalOffset;

                for (var i = 0; i < 8; i++)
                {
                    ScrollViewerSmoothScroll.AdvanceFrameForTesting(scrollViewer);
                    scrollViewer.UpdateLayout();
                }

                scrollViewer.VerticalOffset.Should().BeGreaterThan(
                    offsetAfterImpulse + 5,
                    "inertial scrolling should keep moving after the wheel impulse ends");
            }
            finally
            {
                ScrollViewerSmoothScroll.ResetForTesting();
                host.Close();
            }
        });
    }

    [Fact]
    public void Inertial_velocity_decays_to_zero_after_wheel_impulse()
    {
        WpfStaTestHost.Run(() =>
        {
            var host = new Window { Width = 420, Height = 320, ShowInTaskbar = false };
            try
            {
                var scrollViewer = CreateTallScrollViewer();
                host.Content = scrollViewer;
                WpfStaTestHost.ShowAndWait(host);
                LayoutScrollViewer(scrollViewer);

                ScrollViewerSmoothScroll.Attach(scrollViewer);
                ScrollViewerSmoothScroll.SimulateWheelImpulseForTesting(scrollViewer, delta: -240);

                var peakOffset = scrollViewer.VerticalOffset;
                for (var i = 0; i < 300; i++)
                {
                    ScrollViewerSmoothScroll.AdvanceFrameForTesting(scrollViewer);
                    scrollViewer.UpdateLayout();
                    peakOffset = Math.Max(peakOffset, scrollViewer.VerticalOffset);

                    if (ScrollViewerSmoothScroll.GetVerticalVelocityForTesting(scrollViewer) <= MinVelocityThreshold)
                        break;
                }

                peakOffset.Should().BeGreaterThan(
                    10,
                    "inertial scrolling should move content before friction stops it");
                ScrollViewerSmoothScroll.GetVerticalVelocityForTesting(scrollViewer)
                    .Should().BeApproximately(0, 0.5);
                scrollViewer.VerticalOffset.Should().BeLessThanOrEqualTo(scrollViewer.ScrollableHeight);
            }
            finally
            {
                ScrollViewerSmoothScroll.ResetForTesting();
                host.Close();
            }
        });
    }

    private const double MinVelocityThreshold = 0.15;

    [Fact]
    public void Wheel_impulse_coasts_further_than_before_tuning()
    {
        WpfStaTestHost.Run(() =>
        {
            var host = new Window { Width = 420, Height = 320, ShowInTaskbar = false };
            try
            {
                var scrollViewer = CreateTallScrollViewer();
                host.Content = scrollViewer;
                WpfStaTestHost.ShowAndWait(host);
                LayoutScrollViewer(scrollViewer);

                ScrollViewerSmoothScroll.Attach(scrollViewer);
                ScrollViewerSmoothScroll.SimulateWheelImpulseForTesting(scrollViewer, delta: -120);

                var peakOffset = scrollViewer.VerticalOffset;
                for (var i = 0; i < 300; i++)
                {
                    ScrollViewerSmoothScroll.AdvanceFrameForTesting(scrollViewer);
                    scrollViewer.UpdateLayout();
                    peakOffset = Math.Max(peakOffset, scrollViewer.VerticalOffset);
                }

                peakOffset.Should().BeGreaterThan(
                    40,
                    "tuned inertial scrolling should coast noticeably further on a single wheel notch");
            }
            finally
            {
                ScrollViewerSmoothScroll.ResetForTesting();
                host.Close();
            }
        });
    }

    [Fact]
    public void Settings_window_controller_attaches_smooth_scroll_to_content_viewer()
    {
        var source = File.ReadAllText(Path.Combine(
            SettingsSourcePaths.RepoRoot(),
            "src",
            "SmartGuard.Settings",
            "SettingsWindowController.cs"));

        source.Should().Contain("ScrollViewerSmoothScroll.Attach(Require<ScrollViewer>(window, \"contentScrollViewer\"))");
    }

    [Fact]
    public void Log_view_presenter_attaches_smooth_scroll_to_log_scroll_viewer()
    {
        var source = File.ReadAllText(Path.Combine(
            SettingsSourcePaths.RepoRoot(),
            "src",
            "SmartGuard.Settings",
            "LogViewListPresenter.cs"));

        source.Should().Contain("ScrollViewerSmoothScroll.Attach(ScrollViewer)");
    }

    private static ScrollViewer CreateTallScrollViewer()
    {
        var stack = new StackPanel();
        for (var i = 0; i < 80; i++)
            stack.Children.Add(new TextBlock { Text = $"Line {i}", Margin = new Thickness(0, 0, 0, 8) });

        return new ScrollViewer
        {
            Content = stack,
            Height = 260,
            Width = 380,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    private static void LayoutScrollViewer(ScrollViewer scrollViewer)
    {
        scrollViewer.UpdateLayout();
        scrollViewer.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        scrollViewer.ScrollableHeight.Should().BeGreaterThan(0);
    }
}
