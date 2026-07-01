namespace SmartGuard.Contracts;

public static class DesktopAppBootstrap
{
  public static bool RunSingleInstanceApp(
    string component,
    Func<bool> tryNotifyExisting,
    Action<string> onActivate,
    Action runOwner)
  {
    using var guard = SingleInstanceGuard.TryAcquire(component);
    if (!guard.IsOwner)
      return tryNotifyExisting();

    using var activationCts = new CancellationTokenSource();
    var activationThread = new Thread(() =>
      SingleInstanceActivation.RunActivationServer(component, onActivate, activationCts.Token))
    {
      IsBackground = true,
    };
    activationThread.Start();

    try
    {
      runOwner();
    }
    finally
    {
      activationCts.Cancel();
      activationThread.Join(TimeSpan.FromSeconds(1));
    }

    return true;
  }
}
