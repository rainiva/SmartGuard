namespace SmartGuard.Configuration;

public interface IPowerPlanActivator
{
  void SetActivePlan(Guid planGuid);
}
