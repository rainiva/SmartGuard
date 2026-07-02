namespace SmartGuard.Tray;

public static class TrayNotificationHelper
{
  public static bool PlanChangedForNotification(string? previousPlan, string? currentPlan)
  {
    if (string.IsNullOrWhiteSpace(currentPlan)) return false;
    if (string.IsNullOrWhiteSpace(previousPlan)) return false;
    return !string.Equals(previousPlan, currentPlan, StringComparison.Ordinal);
  }

  public static string FormatPlanChangeBalloon(string planName, int brightness)
    => $"已切换至 {planName}（亮度 {brightness}%）";
}
