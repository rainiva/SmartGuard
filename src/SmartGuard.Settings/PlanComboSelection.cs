namespace SmartGuard.Settings;

internal static class PlanComboSelection
{
  internal static Guid ResolveSelectedGuid(Guid comboSelection, Guid configGuid)
    => comboSelection != Guid.Empty ? comboSelection : configGuid;

  internal static PowerPlanComboItem? FindItem(IReadOnlyList<PowerPlanComboItem> items, Guid selectedGuid)
    => items.FirstOrDefault(item => item.PlanGuid == selectedGuid);
}
