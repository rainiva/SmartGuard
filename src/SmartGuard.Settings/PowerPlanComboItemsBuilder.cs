using SmartGuard.Configuration;

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

  // The label of a selected plan without a name states why: still loading, list unreadable,
  // name unreadable, or confirmed absent on this machine.
  internal static IReadOnlyList<PowerPlanComboItem> Build(PowerPlanCatalogSnapshot snapshot, Guid selectedGuid,
    string? orphanRoleLabel = null)
  {
    var items = snapshot.Names
      .Select(entry => new PowerPlanComboItem { PlanGuid = entry.Key, DisplayName = entry.Value })
      .OrderBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
      .ToList();
    if (selectedGuid == Guid.Empty || snapshot.Names.ContainsKey(selectedGuid)) return items;

    var suffix = snapshot.PresenceOf(selectedGuid) switch
    {
      PowerPlanPresence.Loading => "（正在加载）",
      PowerPlanPresence.ListUnavailable => "（无法读取计划列表）",
      PowerPlanPresence.NameUnreadable => "（名称读取失败）",
      _ => "（未在本机找到）",
    };
    var role = string.IsNullOrWhiteSpace(orphanRoleLabel) ? selectedGuid.ToString() : orphanRoleLabel;
    items.Insert(0, new PowerPlanComboItem { PlanGuid = selectedGuid, DisplayName = role + suffix });
    return items;
  }
}
