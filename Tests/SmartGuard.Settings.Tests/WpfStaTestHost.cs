using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings.Tests;

internal static class WpfStaTestHost
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly object TrackedControllersLock = new();
    private static readonly HashSet<SettingsWindowController> TrackedControllers =
        new(ReferenceEqualityComparer.Instance);
    private static WpfApplicationFixture? _fixture;

    internal static void Attach(WpfApplicationFixture fixture)
        => _fixture = fixture;

    public static void Run(Action action)
    {
        var fixture = _fixture
            ?? throw new InvalidOperationException(
                "WPF tests must use [Collection(\"WpfUiTests\")] so WpfApplicationFixture starts first.");
        fixture.Invoke(() =>
        {
            var failures = new List<Exception>();
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }

            CaptureFailure(AssertQuiescent, failures);
            CaptureFailure(RestoreSharedState, failures);
            lock (TrackedControllersLock)
                TrackedControllers.Clear();

            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1)
                throw new AggregateException("WPF test action or cleanup failed.", failures);
        });
    }

    public static void EnsureApplication()
    {
        var fixture = _fixture
            ?? throw new InvalidOperationException(
                "WPF tests must use [Collection(\"WpfUiTests\")] so WpfApplicationFixture starts first.");
        fixture.Invoke(() => { });
    }

    public static void ShowAndWait(Window window)
    {
        PrepareHeadlessWindow(window);
        window.Show();
        window.UpdateLayout();
        WaitForRender();
        WaitForPriority(DispatcherPriority.ApplicationIdle);
    }

    public static void WaitUntil(
        Func<bool> condition,
        TimeSpan timeout,
        string description)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var started = Stopwatch.GetTimestamp();
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        ExceptionDispatchInfo? conditionFailure = null;
        var conditionSatisfied = false;
        var timedOut = false;

        void EvaluateCondition()
        {
            try
            {
                conditionSatisfied = condition();
                timedOut = !conditionSatisfied && Stopwatch.GetElapsedTime(started) >= timeout;
            }
            catch (Exception ex)
            {
                conditionFailure = ExceptionDispatchInfo.Capture(ex);
            }

            if (conditionSatisfied || timedOut || conditionFailure is not null)
                frame.Continue = false;
        }

        EvaluateCondition();
        if (!conditionSatisfied && !timedOut && conditionFailure is null)
        {
            var timer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(10),
                DispatcherPriority.Background,
                (_, _) => EvaluateCondition(),
                dispatcher);
            try
            {
                timer.Start();
                Dispatcher.PushFrame(frame);
            }
            finally
            {
                timer.Stop();
            }
        }

        if (!conditionSatisfied && conditionFailure is null)
            EvaluateCondition();

        conditionFailure?.Throw();
        if (!conditionSatisfied)
        {
            throw new TimeoutException(
                $"Timed out after {timeout.TotalMilliseconds:0} ms waiting for {description}; {GetDiagnostics()}.");
        }
    }

    public static void WaitForRender()
        => WaitForPriority(DispatcherPriority.Render);

    public static void AssertQuiescent()
    {
        var failures = CloseAllWindows();
        CaptureFailure(
            () => WaitUntil(
                () => GetActiveRefreshCount() == 0,
                DefaultTimeout,
                "active Settings log refreshes to finish"),
            failures);
        CaptureFailure(
            () => WaitForPriority(DispatcherPriority.ApplicationIdle),
            failures);
        CaptureFailure(WaitForRender, failures);
        CaptureFailure(
            () => WaitUntil(
                () => GetWindowCount() == 0,
                DefaultTimeout,
                "all WPF windows to close"),
            failures);

        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
            throw new AggregateException("WPF test cleanup did not become quiescent.", failures);
    }

    public static void PumpDispatcher(Window window)
    {
        if (!window.Dispatcher.CheckAccess())
        {
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            return;
        }

        WaitForPriority(DispatcherPriority.Background);
    }

    internal static void Track(SettingsWindowController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        lock (TrackedControllersLock)
            TrackedControllers.Add(controller);
    }

    internal static void PrepareHeadlessWindow(Window window)
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Opacity = 0;
        window.WindowState = WindowState.Normal;
        // Opacity=0 does not hide windows with a system title bar
        // (AllowsTransparency=false), so park the window far outside the
        // virtual screen to keep test runs from flashing windows at the user.
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = SystemParameters.VirtualScreenLeft - 32000;
        window.Top = SystemParameters.VirtualScreenTop - 32000;
    }

    private static List<Exception> CloseAllWindows()
    {
        var failures = new List<Exception>();
        if (Application.Current is null)
            return failures;

        foreach (var window in Application.Current.Windows.OfType<Window>().ToList())
        {
            try
            {
                window.Close();
            }
            catch (Exception ex)
            {
                failures.Add(new InvalidOperationException(
                    $"Failed to close WPF test window '{window.Title}'.",
                    ex));
            }
        }

        return failures;
    }

    private static void WaitForPriority(DispatcherPriority priority)
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            priority,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static int GetWindowCount()
        => Application.Current?.Windows.Count ?? 0;

    private static int GetActiveRefreshCount()
    {
        lock (TrackedControllersLock)
            return TrackedControllers.Sum(controller => controller.ActiveLogRefreshCountForTests);
    }

    private static int GetUiRefreshInFlightCount()
    {
        lock (TrackedControllersLock)
            return TrackedControllers.Count(controller => controller.IsLogRefreshInFlightForTests);
    }

    private static string GetDiagnostics()
    {
        ThreadPool.GetAvailableThreads(out var availableWorkerThreads, out _);
        return
            $"windows={GetWindowCount()}, active refresh={GetActiveRefreshCount()}, " +
            $"ui refresh in flight={GetUiRefreshInFlightCount()}, " +
            $"log read delay={LogTailReaderTestMetrics.ArtificialDelayMs} ms, " +
            $"log read hook={LogTailReaderTestMetrics.BeforeReadForTests is not null}, " +
            $"thread pool workers available={availableWorkerThreads}, " +
            $"threads={ThreadPool.ThreadCount}, pending={ThreadPool.PendingWorkItemCount}";
    }

    private static void RestoreSharedState()
    {
        SettingsUiTestMode.SetEnabled(true);
        ThemeTransitionAnimator.ResetForTests();
        LogViewTagPalette.ConfigureForDarkMode(false);
        LogTailReaderTestMetrics.Reset();
        SettingsWindowController.ResetTestMetricsForTests();
        // Release any lingering CompositionTarget.Rendering hook and clear
        // residual inertial velocity left by a previous test; otherwise the
        // rendering callback keeps firing on every frame for the remainder
        // of the process and starves the dispatcher of subsequent tests.
        ScrollViewerSmoothScroll.ResetForTesting();
    }

    private static void CaptureFailure(Action action, ICollection<Exception> failures)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }
    }
}
