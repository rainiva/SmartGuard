using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public sealed class WpfStaTestHostTests
{
    [Fact]
    public void WaitUntil_pumps_dispatcher_until_condition_is_true()
    {
        WpfStaTestHost.Run(() =>
        {
            var callbackRan = false;
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => callbackRan = true));

            WpfStaTestHost.WaitUntil(
                () => callbackRan,
                TimeSpan.FromSeconds(1),
                "the queued dispatcher callback");

            callbackRan.Should().BeTrue();
        });
    }

    [Fact]
    public void WaitUntil_rethrows_condition_exception()
    {
        WpfStaTestHost.Run(() =>
        {
            var act = () => WpfStaTestHost.WaitUntil(
                () => throw new InvalidOperationException("condition failed"),
                TimeSpan.FromSeconds(1),
                "the failing condition");

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("condition failed");
        });
    }

    [Fact]
    public void WaitUntil_rechecks_condition_at_timeout_boundary()
    {
        WpfStaTestHost.Run(() =>
        {
            var evaluationCount = 0;
            var act = () => WpfStaTestHost.WaitUntil(
                () => ++evaluationCount > 1,
                TimeSpan.Zero,
                "the timeout-boundary condition");

            act.Should().NotThrow();
            evaluationCount.Should().Be(2);
        });
    }

    [Fact]
    public void WaitUntil_timeout_reports_condition_and_quiescence_diagnostics()
    {
        WpfStaTestHost.Run(() =>
        {
            var act = () => WpfStaTestHost.WaitUntil(
                () => false,
                TimeSpan.FromMilliseconds(25),
                "the sentinel condition");

            act.Should()
                .Throw<TimeoutException>()
                .WithMessage(
                    "*the sentinel condition*windows=*active refresh=*log read delay=*log read hook=*thread pool workers available=*pending=*");
        });
    }

    [Fact]
    public void Run_restores_shared_state_after_closing_windows()
    {
        WpfStaTestHost.Run(() =>
        {
            SettingsUiTestMode.SetEnabled(false);
            ThemeTransitionAnimator.TransitionDuration = TimeSpan.FromMilliseconds(25);
            LogViewTagPalette.ConfigureForDarkMode(true);
            LogTailReaderTestMetrics.ArtificialDelayMs = 25;
            LogTailReaderTestMetrics.BeforeReadForTests = () => { };

            var window = new Window
            {
                Content = new TextBlock { Text = "cleanup sentinel" },
            };
            WpfStaTestHost.ShowAndWait(window);
        });

        WpfStaTestHost.Run(() =>
        {
            SettingsUiTestMode.IsEnabled.Should().BeTrue();
            ThemeTransitionAnimator.TransitionDuration.Should().Be(TimeSpan.FromMilliseconds(760));
            LogViewTagPalette.GetBodyColor().Should().Be(System.Windows.Media.Color.FromRgb(30, 30, 30));
            LogTailReaderTestMetrics.ArtificialDelayMs.Should().Be(0);
            LogTailReaderTestMetrics.BeforeReadForTests.Should().BeNull();
            Application.Current.Windows.Count.Should().Be(0);
        });
    }
}
