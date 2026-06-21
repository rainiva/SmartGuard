namespace SmartGuard.Settings;

public static class PowerPlanComboItemsBuilder
{
  public static IReadOnlyList<PowerPlanComboItem> Build(
    IReadOnlyDictionary<Guid, string> catalog,
    Guid selectedGuid,
    string? orphanRoleLabel = null)
  {
    var items = catalog
      .Select(entry => new PowerPlanComboItem
      {
        PlanGuid = entry.Key,
        DisplayName = entry.Value,
      })
      .OrderBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
      .ToList();

    if (selectedGuid != Guid.Empty && !catalog.ContainsKey(selectedGuid))
    {
      var displayName = string.IsNullOrWhiteSpace(orphanRoleLabel)
        ? $"{selectedGuid}（未在本机找到）"
        : $"{orphanRoleLabel}（未在本机找到）";
      items.Insert(0, new PowerPlanComboItem
      {
        PlanGuid = selectedGuid,
        DisplayName = displayName,
      });
    }

    return items;
  }
}
