using SmartGuard.Configuration;

namespace SmartGuard.Tray;

internal sealed class TrayDisplaySettingsCache
{
  internal static TimeSpan CacheDuration { get; set; } = TimeSpan.FromSeconds(5);

  private readonly Func<TrayNotificationPreferences> _preferencesLoader;
  private DateTime _loadedAt = DateTime.MinValue;
  private TrayNotificationPreferences _preferences;

  public TrayDisplaySettingsCache(GuardConfigRepository repository, string root)
    : this(() =>
    {
      var config = repository.LoadOrDefault(root);
      return new TrayNotificationPreferences(config.NotifyOnPlanChange, config.NotifyOnExternalChange);
    })
  {
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

  private TrayNotificationPreferences LoadIfNeeded()
  {
    if (DateTime.UtcNow - _loadedAt < CacheDuration)
      return _preferences;

    _preferences = _preferencesLoader();
    _loadedAt = DateTime.UtcNow;
    return _preferences;
  }
}
