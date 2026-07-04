namespace SmartGuard.Configuration;

public static class LegacyScheduledTaskCatalog
{
  public static IReadOnlyList<string> TaskNames { get; } =
  [
    "SmartPowerPlan Guardian",
    "SmartPowerPlan Tray",
  ];
}
