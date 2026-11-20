namespace SmartGuard.Configuration;

public static class PowerPlanMappingValidator
{
  public static IReadOnlyList<string> Validate(GuardConfig config)
    => ValidateDuplicates(config);

  public static IReadOnlyList<string> Validate(GuardConfig config, IReadOnlyDictionary<Guid, string> catalog)
  {
    var messages = new List<string>();
    messages.AddRange(ValidateDuplicates(config));
    messages.AddRange(ValidateMissingPlans(config, catalog));
    return messages;
  }

  // Only a confirmed absence is a mapping error; loading, an unreadable list and unreadable names are not.
  internal static IReadOnlyList<string> Validate(GuardConfig config, PowerPlanCatalogSnapshot snapshot)
  {
    var messages = new List<string>(ValidateDuplicates(config));
    foreach (var (plan, role) in new[]
    {
      (config.ActivePlanGuid, PowerPlanCatalogProvider.HighPerformanceDisplayName),
      (config.BalancedPlanGuid, PowerPlanCatalogProvider.BalancedDisplayName),
      (config.PowerSaverPlanGuid, PowerPlanCatalogProvider.PowerSaverDisplayName),
    })
    {
      if (plan != Guid.Empty && snapshot.PresenceOf(plan) == PowerPlanPresence.ConfirmedAbsent)
        messages.Add($"{role}计划未在本机找到");
    }
    return messages;
  }

  public static IReadOnlyList<string> ValidateMissingPlans(
    GuardConfig config,
    IReadOnlyDictionary<Guid, string> catalog)
  {
    var messages = new List<string>();
    AppendMissingPlan(config.ActivePlanGuid, PowerPlanCatalogProvider.HighPerformanceDisplayName, catalog, messages);
    AppendMissingPlan(config.BalancedPlanGuid, PowerPlanCatalogProvider.BalancedDisplayName, catalog, messages);
    AppendMissingPlan(config.PowerSaverPlanGuid, PowerPlanCatalogProvider.PowerSaverDisplayName, catalog, messages);
    return messages;
  }

  private static IReadOnlyList<string> ValidateDuplicates(GuardConfig config)
  {
    var messages = new List<string>();
    if (config.ActivePlanGuid == config.BalancedPlanGuid)
      messages.Add($"{PowerPlanCatalogProvider.HighPerformanceDisplayName}与{PowerPlanCatalogProvider.BalancedDisplayName}不能选择相同电源计划");
    if (config.ActivePlanGuid == config.PowerSaverPlanGuid)
      messages.Add($"{PowerPlanCatalogProvider.HighPerformanceDisplayName}与{PowerPlanCatalogProvider.PowerSaverDisplayName}不能选择相同电源计划");
    if (config.BalancedPlanGuid == config.PowerSaverPlanGuid)
      messages.Add($"{PowerPlanCatalogProvider.BalancedDisplayName}与{PowerPlanCatalogProvider.PowerSaverDisplayName}不能选择相同电源计划");
    return messages;
  }

  private static void AppendMissingPlan(
    Guid planGuid,
    string roleLabel,
    IReadOnlyDictionary<Guid, string> catalog,
    List<string> messages)
  {
    if (planGuid == Guid.Empty) return;
    if (catalog.ContainsKey(planGuid)) return;
    messages.Add($"{roleLabel}计划未在本机找到");
  }
}
