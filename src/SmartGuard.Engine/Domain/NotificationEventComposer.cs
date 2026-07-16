using SmartGuard.Contracts;

namespace SmartGuard.Engine.Domain;

/// <summary>
/// Decides which status notification (if any) an iteration should publish,
/// respecting per-kind user preferences before the event reaches the tray.
/// </summary>
public static class NotificationEventComposer
{
  public static NotificationEvent? Compose(
    bool externalChangeDetected,
    bool planSwitched,
    bool notifyOnPlanChange,
    bool notifyOnExternalChange,
    Func<NotificationEvent> createPlanSwitch,
    Func<NotificationEvent> createExternal)
  {
    if (planSwitched)
      return notifyOnPlanChange ? createPlanSwitch() : null;

    if (externalChangeDetected && notifyOnExternalChange)
      return createExternal();

    return null;
  }

  /// <summary>
  /// Suppresses back-to-back identical notifications (same kind + body) within
  /// <paramref name="cooldown"/> — e.g. Windows flipping Balanced and SmartGuard correcting.
  /// </summary>
  public static NotificationEvent? ApplyCooldown(
    NotificationEvent? candidate,
    NotificationEvent? lastEmitted,
    DateTime lastEmittedAt,
    DateTime now,
    TimeSpan? cooldown = null)
  {
    if (candidate is null)
      return null;

    var window = cooldown ?? TimeSpan.FromMinutes(2);
    if (lastEmitted is not null
        && now - lastEmittedAt < window
        && string.Equals(lastEmitted.kind, candidate.kind, StringComparison.Ordinal)
        && string.Equals(lastEmitted.body, candidate.body, StringComparison.Ordinal))
      return null;

    return candidate;
  }
}
