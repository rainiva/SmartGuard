namespace SmartGuard.Configuration;

internal interface IPowerPlanActivator
{
  PowerSchemeStatus SetActivePlan(Guid planGuid);
}
