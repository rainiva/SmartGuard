namespace SmartGuard.Configuration;

/// <summary>
/// Repository-owned load outcome. Failure kinds are controlled categories, never
/// exception text; the rule fingerprint identifies content, not action authority.
/// </summary>
public sealed record GuardConfigLoadResult(GuardConfig? Config, string? FailureKind, string? RuleFingerprint)
{
  public bool IsValid => Config is not null && FailureKind is null;
}
