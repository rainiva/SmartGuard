using SmartGuard.Configuration;

namespace SmartGuard.Tray;

internal sealed class TrayDisplaySettingsCache
{
  internal static TimeSpan CacheDuration { get; set; } = TimeSpan.FromSeconds(5);

  private readonly Func<bool> _notifyLoader;
  private DateTime _loadedAt = DateTime.MinValue;
  private bool _notifyOnPlanChange;

  public TrayDisplaySettingsCache(GuardConfigRepository repository, string root)
    : this(() => repository.LoadOrDefault(root).NotifyOnPlanChange)
  {
  }

  internal TrayDisplaySettingsCache(Func<bool> notifyLoader)
  {
    _notifyLoader = notifyLoader;
  }

  public bool NotifyOnPlanChange => LoadIfNeeded();

  internal static void ResetForTests()
  {
    CacheDuration = TimeSpan.FromSeconds(5);
  }

  private bool LoadIfNeeded()
  {
    if (DateTime.UtcNow - _loadedAt < CacheDuration)
      return _notifyOnPlanChange;

    _notifyOnPlanChange = _notifyLoader();
    _loadedAt = DateTime.UtcNow;
    return _notifyOnPlanChange;
  }
}
