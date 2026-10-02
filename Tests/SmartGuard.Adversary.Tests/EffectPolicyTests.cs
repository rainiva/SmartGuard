using SmartGuard.Adversary;

namespace SmartGuard.Adversary.Tests;

public class EffectPolicyTests
{
  [Theory]
  [InlineData("source-read", EffectTier.T0, 10, true)]
  [InlineData("temporary-fixture", EffectTier.T1, 100, true)]
  [InlineData("source-read", EffectTier.T1, 1, false)]
  [InlineData("temporary-fixture", EffectTier.T2, 1, false)]
  [InlineData("source-read", EffectTier.T0, 2041, false)]
  [InlineData("source-read", EffectTier.T0, 0, false)]
  [InlineData("unknown", EffectTier.T0, 1, false)]
  [InlineData("temporary-fixture", (EffectTier)99, 1, false)]
  public void Catalog_cannot_grant_itself_effect_authority(string id, EffectTier tier, int cases, bool allowed)
    => Assert.Equal(allowed, EffectPolicy.Allows(new(id, tier, cases)));

  [Fact]
  public void Catalog_has_no_privilege_root_process_or_command_authority()
    => Assert.Equal(new[] { "Id", "MaxCases", "Tier" }, typeof(CatalogAction).GetProperties().Select(p => p.Name).Order().ToArray());
}
