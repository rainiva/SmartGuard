namespace SmartGuard.Configuration;

internal enum PowerPlanCatalogState { Loading, Failed, Loaded }

internal enum PowerPlanPresence { Loading, ListUnavailable, Named, NameUnreadable, ConfirmedAbsent }

/// <summary>Immutable, IO-free view of the latest catalog load; Settings reads only this.</summary>
internal sealed class PowerPlanCatalogSnapshot
{
  private static readonly IReadOnlyDictionary<Guid, string> NoNames = new Dictionary<Guid, string>();
  private static readonly IReadOnlySet<Guid> NoPlans = new HashSet<Guid>();

  internal static readonly PowerPlanCatalogSnapshot Loading = new(PowerPlanCatalogState.Loading, NoNames, NoPlans, NoPlans);
  internal static readonly PowerPlanCatalogSnapshot Failed = new(PowerPlanCatalogState.Failed, NoNames, NoPlans, NoPlans);

  internal PowerPlanCatalogSnapshot(PowerPlanCatalogState state, IReadOnlyDictionary<Guid, string> names,
    IReadOnlySet<Guid> listed, IReadOnlySet<Guid> confirmedAbsent)
  {
    State = state;
    Names = names;
    Listed = listed;
    ConfirmedAbsent = confirmedAbsent;
  }

  internal PowerPlanCatalogState State { get; }
  internal IReadOnlyDictionary<Guid, string> Names { get; }
  internal IReadOnlySet<Guid> Listed { get; }
  internal IReadOnlySet<Guid> ConfirmedAbsent { get; }

  // Absence is a conclusion only after a successful enumeration, which is always a complete list (a list
  // cut at the cap is reported as a failure): a non-built-in scheme that was not listed does not exist;
  // a hidden built-in scheme is absent only when its name read returned Win32 2.
  internal PowerPlanPresence PresenceOf(Guid plan) => State switch
  {
    PowerPlanCatalogState.Loading => PowerPlanPresence.Loading,
    PowerPlanCatalogState.Failed => PowerPlanPresence.ListUnavailable,
    _ when Names.ContainsKey(plan) => PowerPlanPresence.Named,
    _ when Listed.Contains(plan) => PowerPlanPresence.NameUnreadable,
    _ when ConfirmedAbsent.Contains(plan) || !PowerPlanCatalogProvider.IsKnownScheme(plan) => PowerPlanPresence.ConfirmedAbsent,
    _ => PowerPlanPresence.NameUnreadable,
  };
}
