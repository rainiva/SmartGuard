using SmartGuard.Contracts;

namespace SmartGuard.Tray;

public readonly record struct TrayNotificationPreferences(
  bool NotifyOnPlanChange,
  bool NotifyOnExternalChange)
{
  public static bool ShouldNotify(string? kind, bool notifyOnPlanChange, bool notifyOnExternalChange)
    => kind switch
    {
      NotificationKinds.ExternalChange => notifyOnExternalChange,
      NotificationKinds.PlanSwitch => notifyOnPlanChange,
      null or "" => notifyOnPlanChange,
      _ => notifyOnPlanChange,
    };
}
