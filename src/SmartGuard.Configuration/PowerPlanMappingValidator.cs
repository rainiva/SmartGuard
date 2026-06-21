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

  public static IReadOnlyList<string> ValidateMissingPlans(
    GuardConfig config,
    IReadOnlyDictionary<Guid, string> catalog)
  {
    var messages = new List<string>();
    AppendMissingPlan(config.ActivePlanGuid, "高性能", catalog, messages);
    AppendMissingPlan(config.BalancedPlanGuid, "平衡", catalog, messages);
    AppendMissingPlan(config.PowerSaverPlanGuid, "节能", catalog, messages);
    return messages;
  }

  private static IReadOnlyList<string> ValidateDuplicates(GuardConfig config)
  {
    var messages = new List<string>();
    if (config.ActivePlanGuid == config.BalancedPlanGuid)
      messages.Add("高性能与平衡不能选择相同电源计划");
    if (config.ActivePlanGuid == config.PowerSaverPlanGuid)
      messages.Add("高性能与节能不能选择相同电源计划");
    if (config.BalancedPlanGuid == config.PowerSaverPlanGuid)
      messages.Add("平衡与节能不能选择相同电源计划");
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
