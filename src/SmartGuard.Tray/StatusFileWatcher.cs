namespace SmartGuard.Tray;

internal sealed class StatusFileWatcher : IDisposable
{
  private readonly FileSystemWatcher _watcher;
  private readonly Action _onChanged;
  private DateTime _lastFireUtc = DateTime.MinValue;

  public StatusFileWatcher(string statusPath, Action onChanged)
  {
    _onChanged = onChanged;
    var directory = Path.GetDirectoryName(statusPath);
    var fileName = Path.GetFileName(statusPath);
    if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
    {
      _watcher = new FileSystemWatcher { EnableRaisingEvents = false };
      return;
    }

    _watcher = new FileSystemWatcher(directory, fileName)
    {
      NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
      EnableRaisingEvents = true,
    };
    _watcher.Changed += OnChanged;
    _watcher.Created += OnChanged;
    _watcher.Renamed += OnChanged;
  }

  private void OnChanged(object sender, FileSystemEventArgs e)
  {
    var now = DateTime.UtcNow;
    if ((now - _lastFireUtc).TotalMilliseconds < 250) return;
    _lastFireUtc = now;
    _onChanged();
  }

  public void Dispose()
  {
    _watcher.EnableRaisingEvents = false;
    _watcher.Dispose();
  }
}
