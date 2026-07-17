namespace SmartGuard.Configuration;

public static class ScheduledTaskInstallGate
{
  public const string EnvVarName = "SMARTGUARD_SKIP_SCHEDULED_TASKS";

  public static bool ShouldSkip(bool cliFlag, string? envValue)
    => cliFlag || IsTruthyEnv(envValue);

  public static bool IsTruthyEnv(string? value)
  {
    if (string.IsNullOrWhiteSpace(value)) return false;
    return value.Equals("1", StringComparison.OrdinalIgnoreCase)
      || value.Equals("true", StringComparison.OrdinalIgnoreCase)
      || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
      || value.Equals("on", StringComparison.OrdinalIgnoreCase);
  }

  public static bool ShouldSkipFromEnvironment(bool cliFlag)
    => ShouldSkip(cliFlag, Environment.GetEnvironmentVariable(EnvVarName));
}
