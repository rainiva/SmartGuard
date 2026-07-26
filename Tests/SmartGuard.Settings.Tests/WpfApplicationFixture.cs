using System.Windows;
using System.Windows.Threading;

namespace SmartGuard.Settings.Tests;

internal sealed class WpfApplicationFixture : IDisposable
{
    private readonly Thread _staThread;
    private readonly ManualResetEventSlim _ready = new(false);

    public WpfApplicationFixture()
    {
        // Blocking-hook tests (10s gated log reads) pile up busy pool workers;
        // once the worker count passes the default minimum (CPU count), the
        // thread pool throttles new-thread injection to ~1/s and a fresh
        // Task.Run log flight can sit "queued" for seconds, failing WaitUntil
        // gates (observed: flight stage=queued+5017ms, pending=18). Raise the
        // floor so refresh flights always schedule promptly under test load.
        ThreadPool.GetMinThreads(out var minWorkers, out var minIo);
        ThreadPool.SetMinThreads(Math.Max(minWorkers, 128), minIo);

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
