namespace SmartGuard.Configuration;

public static class ConfigFileWatcher
{
  public static FileSystemWatcher? Watch(string path, Action onChanged)
  {
    var dir = Path.GetDirectoryName(path);
    var fileName = Path.GetFileName(path);
    if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(fileName))
      return null;

    var watcher = new FileSystemWatcher(dir, fileName)
    {
      NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
    };
    FileSystemEventHandler handler = (_, _) => onChanged();
    watcher.Changed += handler;
    watcher.Created += handler;
    watcher.Deleted += handler;
    watcher.Renamed += (_, _) => onChanged();
    watcher.EnableRaisingEvents = true;
    return watcher;
  }
}
