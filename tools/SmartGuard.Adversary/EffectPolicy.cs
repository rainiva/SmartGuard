namespace SmartGuard.Adversary;

public static class EffectPolicy
{
  public static bool Allows(CatalogAction action)
  {
    if (action is null || action.MaxCases is < 1 or > 2040) return false;
    // These are capability ceilings, not implemented executors or catalog authority.
    return action.Id switch
    {
      "source-read" => action.Tier == EffectTier.T0,
      "temporary-fixture" => action.Tier == EffectTier.T1,
      _ => false
    };
  }
}
