namespace SmartGuard.Configuration;

public sealed record LogRetentionPolicy(
  long MaxBytes,
  int RetainDays,
  int MaxArchiveFiles,
  long MaxDirectoryBytes)
{
  public static LogRetentionPolicy FromGuardConfig(GuardConfig config)
    => new(
      config.LogMaxBytes,
      Math.Clamp(config.LogRetentionDays, 1, 365),
      config.LogMaxArchiveFiles,
      config.LogDirectoryMaxBytes);
}
