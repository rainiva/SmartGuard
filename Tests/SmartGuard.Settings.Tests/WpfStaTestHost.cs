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
}
