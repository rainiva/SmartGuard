using SmartGuard.Configuration;

namespace SmartGuard.Tray;

internal sealed class TrayDisplaySettingsCache
{
  internal static TimeSpan CacheDuration { get; set; } = TimeSpan.FromSeconds(5);

  private readonly Func<TrayNotificationPreferences> _preferencesLoader;
  private readonly FileSystemWatcher? _configWatcher;
  private DateTime _loadedAt = DateTime.MinValue;
  private TrayNotificationPreferences _preferences;

  public TrayDisplaySettingsCache(GuardConfigRepository repository, string root)
    : this(() =>
    {
      var config = repository.LoadOrDefault(root);
      return new TrayNotificationPreferences(config.NotifyOnPlanChange, config.NotifyOnExternalChange);
    })
  {
    var configPath = SmartGuardPaths.ConfigFile(root);
    _configWatcher = CreateConfigWatcher(configPath, Invalidate);
  }

  public TrayDisplaySettingsCache(
    TrayNotificationPreferences initialPreferences,
    Func<TrayNotificationPreferences> preferencesLoader)
  {
    _preferences = initialPreferences;
    _preferencesLoader = preferencesLoader;
    _loadedAt = DateTime.UtcNow;
  }

  internal TrayDisplaySettingsCache(Func<TrayNotificationPreferences> preferencesLoader)
  {
    _preferencesLoader = preferencesLoader;
  }

  public bool NotifyOnPlanChange => LoadIfNeeded().NotifyOnPlanChange;

  public bool NotifyOnExternalChange => LoadIfNeeded().NotifyOnExternalChange;

  internal static void ResetForTests()
  {
    CacheDuration = TimeSpan.FromSeconds(5);
  }

  internal void Invalidate() => _loadedAt = DateTime.MinValue;

  private TrayNotificationPreferences LoadIfNeeded()
  {
    if (DateTime.UtcNow - _loadedAt < CacheDuration)
      return _preferences;

    _preferences = _preferencesLoader();
    _loadedAt = DateTime.UtcNow;
    return _preferences;
  }

  private static FileSystemWatcher? CreateConfigWatcher(string path, Action onChanged)
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
