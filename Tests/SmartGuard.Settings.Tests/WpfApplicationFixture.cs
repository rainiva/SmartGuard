using System.Windows;
using System.Windows.Threading;

namespace SmartGuard.Settings.Tests;

internal sealed class WpfApplicationFixture : IDisposable
{
    private readonly Thread _staThread;
    private readonly ManualResetEventSlim _ready = new(false);

    public WpfApplicationFixture()
    {
        _staThread = new Thread(StaThreadMain)
        {
            IsBackground = true,
            Name = "SmartGuard.Settings.WpfTests",
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
        _ready.Wait();
        WpfStaTestHost.Attach(this);
    }

    private void StaThreadMain()
    {
        _ = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        _ready.Set();
        Dispatcher.Run();
    }

    public void Invoke(Action action)
    {
        if (Thread.CurrentThread == _staThread)
        {
            action();
            return;
        }

        Application.Current!.Dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        if (Application.Current is { } app)
            app.Dispatcher.InvokeShutdown();

        _staThread.Join(TimeSpan.FromSeconds(5));
    }
}
