using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace SmartGuard.Settings.Tests;

internal static class WpfStaTestHost
{
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
            try
            {
                action();
            }
            finally
            {
                CloseAllWindows();
                DrainBackgroundWork();
            }
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
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    public static void WaitUntil(
        Func<bool> condition,
        Window? window,
        string description,
        int timeoutMs = 10000,
        bool pumpDispatcher = true)
    {
        var started = Stopwatch.GetTimestamp();
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);

        while (Stopwatch.GetElapsedTime(started) < timeout)
        {
            if (condition())
                return;

            if (pumpDispatcher && window is not null)
                PumpDispatcher(window);

            Thread.Sleep(1);
        }

        if (!condition())
            throw new TimeoutException($"Timed out waiting for {description}.");
    }

    public static void PumpDispatcher(Window window)
    {
        if (!window.Dispatcher.CheckAccess())
        {
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            return;
        }

        var frame = new DispatcherFrame();
        _ = window.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    internal static void PrepareHeadlessWindow(Window window)
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Opacity = 0;
        window.WindowState = WindowState.Normal;
        window.Left = 0;
        window.Top = 0;
    }

    private static void CloseAllWindows()
    {
        if (Application.Current is null)
            return;

        foreach (var window in Application.Current.Windows.OfType<Window>().ToList())
        {
            try
            {
                window.Close();
            }
            catch
            {
                // Best-effort cleanup between automated UI tests.
            }
        }
    }

    private static void DrainBackgroundWork()
    {
        for (var i = 0; i < 3; i++)
        {
            Thread.Sleep(5);
            Dispatcher.CurrentDispatcher.Invoke(
                () => { },
                DispatcherPriority.Background);
        }
    }
}
