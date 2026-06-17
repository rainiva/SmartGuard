namespace SmartGuard.Contracts;

public sealed class SingleInstanceGuard : IDisposable
{
  private readonly Mutex _mutex;

  public bool IsOwner { get; }

  private SingleInstanceGuard(Mutex mutex, bool isOwner)
  {
    _mutex = mutex;
    IsOwner = isOwner;
  }

  public static SingleInstanceGuard TryAcquire(string component)
  {
    var name = $"Global\\SmartGuard.{component}";
    var mutex = new Mutex(false, name);
    var owned = mutex.WaitOne(0, false);
    return new SingleInstanceGuard(mutex, owned);
  }

  public void Dispose()
  {
    if (IsOwner)
    {
      try { _mutex.ReleaseMutex(); } catch { /* ignore */ }
    }
    _mutex.Dispose();
  }
}
