using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class ScrollViewerSmoothScrollTests
{
    [Theory]
    [InlineData(-120, false, true)]
    [InlineData(120, true, true)]
    [InlineData(-120, true, false)]
    [InlineData(120, false, false)]
    public void Nested_program_list_routes_wheel_to_the_list_or_the_page_at_its_boundary(int delta, bool startAtEnd, bool listCanScroll)
    {
        WpfStaTestHost.Run(() =>
        {
            var host = new Window { Width = 420, Height = 320, ShowInTaskbar = false };
            try
            {
                var list = new ListBox { Height = 110 };
                ScrollViewer.SetCanContentScroll(list, false);
                for (var index = 0; index < 40; index++) list.Items.Add($"Program {index}.exe");
                var stack = new StackPanel();
                stack.Children.Add(list);
                stack.Children.Add(new Border { Height = 600 });
                var outer = new ScrollViewer { Content = stack, Height = 260 };
                host.Content = outer;
                WpfStaTestHost.ShowAndWait(host);
                var inner = Descendant<ScrollViewer>(list)!;
                inner.Should().NotBeNull();
                inner.ScrollableHeight.Should().BeGreaterThan(0);
                if (startAtEnd) inner.ScrollToEnd();
                inner.UpdateLayout();
                var before = inner.VerticalOffset;
                if (!listCanScroll && delta > 0)
                {
                    outer.ScrollToEnd();
                    outer.UpdateLayout();
                }
                ScrollViewerSmoothScroll.Attach(outer);
                var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
                { RoutedEvent = UIElement.PreviewMouseWheelEvent };
                inner.RaiseEvent(wheel);
                if (!listCanScroll)
                {
                    wheel.Handled.Should().BeTrue("at the list boundary the page should consume the gesture");
                    ScrollViewerSmoothScroll.GetVerticalVelocityForTesting(outer).Should().NotBe(0);
                    return;
                }
                wheel.Handled.Should().BeFalse("the outer tunnel handler must allow the list to consume this direction");
                wheel.RoutedEvent = UIElement.MouseWheelEvent;
                inner.RaiseEvent(wheel);
                inner.UpdateLayout();
                Math.Abs(inner.VerticalOffset - before).Should().BeGreaterThan(0);
                ScrollViewerSmoothScroll.GetVerticalVelocityForTesting(outer).Should().Be(0);
            }
            finally { ScrollViewerSmoothScroll.ResetForTesting(); host.Close(); }
        });
    }

    private static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            if (Descendant<T>(child) is T nested) return nested;
        }
        return null;
    }

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
    public void Log_view_presenter_does_not_attach_custom_inertia_to_log_scroll_viewer()
    {
        var source = File.ReadAllText(Path.Combine(
            SettingsSourcePaths.RepoRoot(),
            "src",
            "SmartGuard.Settings",
            "LogViewListPresenter.cs"));

        source.Should().NotContain("ScrollViewerSmoothScroll.Attach(ScrollViewer)");
    }

    [Fact]
    public void Log_view_wheel_event_is_left_to_native_pixel_scrolling()
    {
        WpfStaTestHost.Run(() =>
        {
            var host = new Window { Width = 420, Height = 320, ShowInTaskbar = false };
            try
            {
                var listBox = new ListBox { Height = 260, Width = 380 };
                var presenter = new LogViewListPresenter();
                presenter.Attach(listBox);
                presenter.Apply(LogViewUpdatePlan.ReplaceAll(
                    Enumerable.Range(0, 500)
                        .Select(i => $"[INFO] 2026-08-02 10:00:00 line {i} {new string('x', 300)}")
                        .ToArray()));
                host.Content = listBox;
                WpfStaTestHost.ShowAndWait(host);
                presenter.EnsureScrollViewerResolved();
                var scrollViewer = presenter.ScrollViewer!;
                scrollViewer.ScrollToEnd();
                scrollViewer.UpdateLayout();

                var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent,
                    Source = scrollViewer,
                };
                scrollViewer.RaiseEvent(wheel);

                wheel.Handled.Should().BeFalse(
                    "the log list should use native pixel scrolling instead of swallowing the gesture");
                ScrollViewerSmoothScroll.GetVerticalVelocityForTesting(scrollViewer)
                    .Should().Be(0);
            }
            finally
            {
                ScrollViewerSmoothScroll.ResetForTesting();
                host.Close();
            }
        });
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
