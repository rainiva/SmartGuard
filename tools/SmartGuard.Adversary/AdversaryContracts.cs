namespace SmartGuard.Adversary;

public enum EffectTier { T0, T1, T2, T3 }
public enum ResultState
{
  NotRun, Passed, ConfirmedFinding, FlakyFinding, Blocked,
  InfrastructureFailed, SelfCheckFailed, RestoreFailed
}
public sealed record AdversaryRequest(EffectTier[] RequestedLayers, int MaxCases = 100);
// Child observation has no final verdict, trust level or authoritative exit code.
public sealed record LayerObservation(EffectTier Tier, int Executed, int Remaining, string BlockReason);
public sealed record AdversaryObservation(LayerObservation[] Layers);
// A pure parent-side model, not an attestation or independent verifier.
public sealed record ParentLayerEvidence(EffectTier Tier, ResultState State, int Executed, int Remaining = 0);
public sealed record ParentEvaluation(ResultState State, int ExitCode);
public sealed record CatalogAction(string Id, EffectTier Tier, int MaxCases);
