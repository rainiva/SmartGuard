namespace SmartGuard.Configuration;

internal sealed class PowerPlanActivator : IPowerPlanActivator
{
  // Tray manual switch through PowerSchemeService (PWR-INV-SCHEME-API); bounded by its deadline.
  public PowerSchemeStatus SetActivePlan(Guid planGuid)
    => PowerSchemeService.SetActiveAsync(planGuid).GetAwaiter().GetResult().Status;
}
